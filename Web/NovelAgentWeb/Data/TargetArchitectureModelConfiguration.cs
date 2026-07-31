using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Data;

internal static class TargetArchitectureModelConfiguration
{
    public static void ConfigureTargetArchitecture(this ModelBuilder modelBuilder)
    {
        ConfigureCreativeGoals(modelBuilder);
        ConfigureGoalRevisions(modelBuilder);
        ConfigureTaskGraphs(modelBuilder);
        ConfigureKernelTasks(modelBuilder);
        ConfigureKernelArtifacts(modelBuilder);
        ConfigureDomainEvents(modelBuilder);
        ConfigureModelExecutions(modelBuilder);
        ConfigureCanonBranches(modelBuilder);
        ConfigureCandidateChapters(modelBuilder);
        ConfigureCandidateAcceptances(modelBuilder);
        ConfigureBranchMergeRecords(modelBuilder);
        ConfigureContinuitySummaries(modelBuilder);
        ConfigureCanonChanges(modelBuilder);
        ConfigureGoalContextSnapshots(modelBuilder);
        ConfigureModelKernelConfigurations(modelBuilder);
        ConfigureReworkIntents(modelBuilder);
        ConfigureKnowledgeIngestion(modelBuilder);
        ConfigureVectorIndexRecords(modelBuilder);
        ConfigureCollaborationMemory(modelBuilder);
    }

    private static void ConfigureCollaborationMemory(ModelBuilder modelBuilder)
    {
        var author = modelBuilder.Entity<AuthorMemory>();
        author.ToTable("author_memories");
        author.HasKey(x => x.Id);
        foreach (var property in author.Metadata.GetProperties())
            property.SetColumnName(ToSnakeCase(property.Name));
        author.Property(x => x.UserId).IsRequired();
        author.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        Json(author, x => x.ContentJson);
        author.HasIndex(x => new { x.UserId, x.MemoryKind, x.Version }).IsUnique()
            .HasDatabaseName("ux_author_memories_kind_version");

        var decisions = ConfigureUserScoped<ProjectCollaborationDecision>(modelBuilder, "project_collaboration_decisions");
        Json(decisions, x => x.ContentJson);
        decisions.HasIndex(x => new { x.UserId, x.ProjectId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_project_collaboration_decisions_active");
        decisions.HasIndex(x => new { x.UserId, x.SourceSuggestionId, x.Scope }).IsUnique()
            .HasFilter("source_suggestion_id IS NOT NULL")
            .HasDatabaseName("ux_project_collaboration_decisions_suggestion_scope");

        var sessions = ConfigureUserScoped<SessionDialogueState>(modelBuilder, "session_dialogue_states");
        Json(sessions, x => x.ContentJson);
        sessions.HasIndex(x => new { x.UserId, x.ProjectId, x.SessionId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_session_dialogue_states_scope");

        var observations = ConfigureUserScoped<ExperienceObservation>(modelBuilder, "experience_observations");
        Json(observations, x => x.EvidenceJson);
        Json(observations, x => x.MetricsJson);
        observations.HasIndex(x => new { x.UserId, x.ProjectId, x.ObservationType, x.CreatedAt })
            .HasDatabaseName("ix_experience_observations_type");

        var suggestions = ConfigureUserScoped<ExperienceSuggestion>(modelBuilder, "experience_suggestions");
        Json(suggestions, x => x.ProposedChangeJson);
        suggestions.Property(x => x.DecisionVersion).IsConcurrencyToken();
        suggestions.HasIndex(x => new { x.UserId, x.ProjectId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_experience_suggestions_status");
        suggestions.HasIndex(x => new { x.UserId, x.ProjectId, x.SuppressionFingerprint, x.Status })
            .HasDatabaseName("ix_experience_suggestions_suppression");
    }

    private static void ConfigureVectorIndexRecords(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<VectorIndexRecord>(modelBuilder, "vector_index_records");
        entity.HasIndex(x => new { x.UserId, x.QdrantPointId }).IsUnique()
            .HasDatabaseName("ux_vector_index_records_user_point");
        entity.Property(x => x.SourceDocumentId).IsRequired();
        entity.HasIndex(x => new { x.UserId, x.SourceDocumentId, x.SourceType, x.SourceId, x.ChunkIndex, x.EmbeddingVersion }).IsUnique()
            .HasDatabaseName("ux_vector_index_records_source_version");
        entity.HasIndex(x => new { x.UserId, x.Status, x.UpdatedAt })
            .HasDatabaseName("ix_vector_index_records_status");
        entity.HasOne<KnowledgeDocumentBlob>().WithMany().HasForeignKey(x => x.DocumentBlobId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureKnowledgeIngestion(ModelBuilder modelBuilder)
    {
        var blobs = ConfigureUserScoped<KnowledgeDocumentBlob>(modelBuilder, "knowledge_document_blobs");
        blobs.Property(x => x.Data).HasColumnType("bytea");
        blobs.HasIndex(x => new { x.UserId, x.KnowledgeVersion }).IsUnique()
            .HasDatabaseName("ux_knowledge_document_blobs_user_version");

        var sections = ConfigureUserScoped<KnowledgeSection>(modelBuilder, "knowledge_sections");
        sections.HasIndex(x => new { x.UserId, x.DocumentBlobId, x.SectionIndex }).IsUnique()
            .HasDatabaseName("ux_knowledge_sections_document_index");
        sections.HasOne<KnowledgeDocumentBlob>().WithMany().HasForeignKey(x => x.DocumentBlobId)
            .OnDelete(DeleteBehavior.Cascade);

        var chunks = ConfigureUserScoped<KnowledgeChunk>(modelBuilder, "knowledge_chunks");
        chunks.HasIndex(x => new { x.UserId, x.DocumentBlobId, x.ChunkIndex }).IsUnique()
            .HasDatabaseName("ux_knowledge_chunks_document_index");
        chunks.HasIndex(x => new { x.UserId, x.SectionId, x.CharStart })
            .HasDatabaseName("ix_knowledge_chunks_section_order");
        chunks.HasOne<KnowledgeDocumentBlob>().WithMany().HasForeignKey(x => x.DocumentBlobId)
            .OnDelete(DeleteBehavior.Cascade);
        chunks.HasOne<KnowledgeSection>().WithMany().HasForeignKey(x => x.SectionId)
            .OnDelete(DeleteBehavior.Cascade);

        var entries = ConfigureUserScoped<KnowledgeEntry>(modelBuilder, "knowledge_entries");
        entries.HasIndex(x => new { x.UserId, x.LogicalKnowledgeId, x.Version }).IsUnique()
            .HasDatabaseName("ux_knowledge_entries_logical_version");
        entries.HasIndex(x => new { x.UserId, x.Status, x.KnowledgeVersion })
            .HasDatabaseName("ix_knowledge_entries_status_version");
        entries.HasOne<KnowledgeDocumentBlob>().WithMany().HasForeignKey(x => x.DocumentBlobId)
            .OnDelete(DeleteBehavior.Cascade);
        entries.HasOne<KnowledgeBase>().WithMany().HasForeignKey(x => x.LogicalKnowledgeId)
            .OnDelete(DeleteBehavior.Restrict);

        var styles = ConfigureUserScoped<StyleProfile>(modelBuilder, "style_profiles");
        Json(styles, x => x.FeaturesJson);
        styles.HasIndex(x => new { x.UserId, x.DocumentBlobId, x.Version }).IsUnique()
            .HasDatabaseName("ux_style_profiles_document_version");
        styles.HasOne<KnowledgeDocumentBlob>().WithMany().HasForeignKey(x => x.DocumentBlobId)
            .OnDelete(DeleteBehavior.Cascade);

        var citations = ConfigureUserScoped<KnowledgeCitation>(modelBuilder, "knowledge_citations");
        citations.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique()
            .HasDatabaseName("ux_knowledge_citations_idempotency");
        citations.HasIndex(x => new { x.UserId, x.ProjectId, x.ChapterVersionId })
            .HasDatabaseName("ix_knowledge_citations_chapter_version");
        citations.HasOne<KnowledgeEntry>().WithMany().HasForeignKey(x => x.KnowledgeEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureReworkIntents(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<ReworkIntent>(modelBuilder, "rework_intents");
        Json(entity, x => x.PreserveJson);
        Json(entity, x => x.MayChangeJson);
        Json(entity, x => x.MustNotChangeJson);
        Json(entity, x => x.AcceptanceCriteriaJson);
        Json(entity, x => x.ImpactAssessmentJson);
        entity.Property(x => x.AttemptCount).IsConcurrencyToken();
        entity.HasIndex(x => new { x.UserId, x.CandidateChapterId, x.CandidateVersion, x.Status })
            .HasDatabaseName("ix_rework_intents_candidate_status");
    }

    private static void ConfigureCreativeGoals(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<CreativeGoal>(modelBuilder, "creative_goals");
        entity.Property(x => x.TotalCostLimit).HasPrecision(18, 6);
        entity.Property(x => x.ReservedCost).HasPrecision(18, 6);
        entity.Property(x => x.ActualCost).HasPrecision(18, 6);
        entity.Property(x => x.AggregateVersion).IsConcurrencyToken();
        Json(entity, x => x.TargetChapterRangeJson);
        Json(entity, x => x.SuccessCriteriaJson);
        Json(entity, x => x.MustPreserveJson);
        Json(entity, x => x.MustHappenJson);
        Json(entity, x => x.MustNotChangeJson);
        Json(entity, x => x.AcceptancePolicyJson);
        Json(entity, x => x.ReworkPolicyJson);
        Json(entity, x => x.ModelConfigVersionsJson);
        Json(entity, x => x.ProtocolVersionsJson);
        entity.HasIndex(x => new { x.UserId, x.ProjectId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key <> ''")
            .HasDatabaseName("ux_creative_goals_scope_idempotency");
        entity.HasIndex(x => new { x.UserId, x.ProjectId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_creative_goals_scope_status");
    }

    private static void ConfigureGoalRevisions(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<GoalRevision>(modelBuilder, "goal_revisions");
        Json(entity, x => x.ConstraintChangesJson);
        Json(entity, x => x.ReusableArtifactIdsJson);
        Json(entity, x => x.InvalidatedArtifactIdsJson);
        Json(entity, x => x.AffectedNodeIdsJson);
        entity.HasIndex(x => new { x.UserId, x.GoalId, x.RevisionNumber })
            .IsUnique()
            .HasDatabaseName("ux_goal_revisions_goal_number");
    }

    private static void ConfigureTaskGraphs(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<TaskGraphVersion>(modelBuilder, "task_graph_versions");
        Json(entity, x => x.GraphJson);
        entity.HasIndex(x => new { x.UserId, x.GoalId, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_task_graph_versions_goal_version");
        entity.HasIndex(x => new { x.UserId, x.GoalId })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_task_graph_versions_active_goal");
    }

    private static void ConfigureKernelTasks(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<KernelTask>(modelBuilder, "kernel_tasks");
        Json(entity, x => x.DependencyTaskIdsJson);
        Json(entity, x => x.InputArtifactIdsJson);
        Json(entity, x => x.OutputArtifactIdsJson);
        entity.HasIndex(x => new { x.UserId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_kernel_tasks_idempotency");
        entity.HasIndex(x => new { x.Status, x.NextAttemptAt, x.Priority, x.LeaseExpiresAt, x.CreatedAt })
            .HasDatabaseName("ix_kernel_tasks_claim");
        entity.HasIndex(x => new { x.UserId, x.GoalId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_kernel_tasks_goal_status");
    }

    private static void ConfigureKernelArtifacts(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<KernelArtifact>(modelBuilder, "kernel_artifacts");
        Json(entity, x => x.ContentJson);
        entity.HasIndex(x => new { x.UserId, x.GoalId, x.ContentHash, x.ArtifactType, x.SchemaVersion })
            .HasDatabaseName("ix_kernel_artifacts_content");
        entity.HasIndex(x => new { x.UserId, x.TaskId, x.CreatedAt })
            .HasDatabaseName("ix_kernel_artifacts_task");
    }

    private static void ConfigureDomainEvents(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<DomainEvent>(modelBuilder, "domain_events");
        Json(entity, x => x.ArtifactRefsJson);
        Json(entity, x => x.EvidenceRefsJson);
        Json(entity, x => x.PayloadJson);
        entity.HasIndex(x => new { x.UserId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_domain_events_idempotency");
        entity.HasIndex(x => new { x.UserId, x.AggregateType, x.AggregateId, x.AggregateVersion })
            .IsUnique()
            .HasDatabaseName("ux_domain_events_aggregate_version");
    }

    private static void ConfigureModelExecutions(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<ModelExecution>(modelBuilder, "model_executions");
        entity.Property(x => x.ReservedCost).HasPrecision(18, 6);
        entity.Property(x => x.ActualCost).HasPrecision(18, 6);
        entity.Property(x => x.ResultJson).HasColumnType("jsonb");
        entity.HasIndex(x => new { x.UserId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_model_executions_idempotency");
        entity.HasIndex(x => new { x.UserId, x.GoalId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_model_executions_goal_status");
        entity.HasIndex(x => new { x.Status, x.LeaseExpiresAt, x.CreatedAt })
            .HasDatabaseName("ix_model_executions_recovery");
        entity.HasIndex(x => new { x.Provider, x.ProviderRequestId })
            .HasDatabaseName("ix_model_executions_provider_request");
    }

    private static void ConfigureCanonBranches(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<CanonBranch>(modelBuilder, "canon_branches");
        entity.HasIndex(x => new { x.UserId, x.GoalId })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_canon_branches_active_goal");
        entity.HasIndex(x => new { x.UserId, x.ProjectId, x.Status, x.CreatedAt })
            .HasDatabaseName("ix_canon_branches_project_status");
    }

    private static void ConfigureCandidateChapters(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<CandidateChapter>(modelBuilder, "candidate_chapters");
        Json(entity, x => x.ReviewArtifactIdsJson);
        entity.HasIndex(x => new { x.UserId, x.BranchId, x.ChapterNumber, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_candidate_chapters_branch_number_version");
        entity.HasIndex(x => new { x.UserId, x.BranchId, x.Status, x.ChapterNumber })
            .HasDatabaseName("ix_candidate_chapters_branch_status");
    }

    private static void ConfigureCandidateAcceptances(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<CandidateAcceptance>(modelBuilder, "candidate_acceptances");
        entity.HasIndex(x => new { x.UserId, x.CandidateChapterId, x.CandidateVersion })
            .IsUnique()
            .HasDatabaseName("ux_candidate_acceptances_chapter_version");
    }

    private static void ConfigureBranchMergeRecords(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<BranchMergeRecord>(modelBuilder, "branch_merge_records");
        Json(entity, x => x.CandidateVersionsJson);
        entity.HasIndex(x => new { x.UserId, x.BranchId, x.CreatedAt })
            .HasDatabaseName("ix_branch_merge_records_branch");
    }

    private static void ConfigureContinuitySummaries(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<ContinuitySummary>(modelBuilder, "continuity_summaries");
        Json(entity, x => x.SummaryJson);
        Json(entity, x => x.EvidenceRefsJson);
        entity.HasIndex(x => new { x.UserId, x.ChapterVersionId, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_continuity_summaries_chapter_version");
    }

    private static void ConfigureCanonChanges(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<CanonChange>(modelBuilder, "canon_changes");
        Json(entity, x => x.ChangeJson);
        Json(entity, x => x.EvidenceRefsJson);
        entity.HasIndex(x => new { x.UserId, x.ProjectId, x.ChangeType, x.CreatedAt })
            .HasDatabaseName("ix_canon_changes_project_type");
    }

    private static void ConfigureGoalContextSnapshots(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<GoalContextSnapshot>(modelBuilder, "goal_context_snapshots");
        Json(entity, x => x.ModelConfigVersionsJson);
        Json(entity, x => x.ProtocolVersionsJson);
        Json(entity, x => x.ContentHashesJson);
        entity.HasIndex(x => new { x.UserId, x.GoalId })
            .IsUnique()
            .HasDatabaseName("ux_goal_context_snapshots_goal");
    }

    private static void ConfigureModelKernelConfigurations(ModelBuilder modelBuilder)
    {
        var entity = ConfigureUserScoped<ModelKernelConfiguration>(modelBuilder, "model_kernel_configurations");
        Json(entity, x => x.FallbackJson);
        entity.Property(x => x.InputPricePerMillion).HasPrecision(18, 6);
        entity.Property(x => x.OutputPricePerMillion).HasPrecision(18, 6);
        entity.HasIndex(x => new { x.UserId, x.ProjectId, x.KernelName, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_model_kernel_configurations_version");
        entity.HasIndex(x => new { x.UserId, x.ProjectId, x.KernelName })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_model_kernel_configurations_active");
    }

    private static EntityTypeBuilder<TEntity> ConfigureUserScoped<TEntity>(
        ModelBuilder modelBuilder,
        string tableName)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(tableName);
        entity.HasKey("Id");
        entity.Property<string>("UserId").IsRequired();
        entity.Property<string>("ProjectId").IsRequired();

        foreach (var property in entity.Metadata.GetProperties())
            property.SetColumnName(ToSnakeCase(property.Name));

        var createdAt = entity.Metadata.FindProperty("CreatedAt");
        createdAt?.SetDefaultValueSql("CURRENT_TIMESTAMP");
        return entity;
    }

    private static void Json<TEntity>(
        EntityTypeBuilder<TEntity> entity,
        System.Linq.Expressions.Expression<Func<TEntity, string>> property)
        where TEntity : class =>
        entity.Property(property).HasColumnType("jsonb");

    private static string ToSnakeCase(string name)
    {
        var result = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            if (char.IsUpper(current) && i > 0)
                result.Append('_');
            result.Append(char.ToLowerInvariant(current));
        }

        return result.ToString();
    }
}
