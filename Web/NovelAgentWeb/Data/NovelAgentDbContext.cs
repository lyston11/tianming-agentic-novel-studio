using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Data;

public class NovelAgentDbContext : DbContext
{
    public NovelAgentDbContext(DbContextOptions<NovelAgentDbContext> options)
        : base(options)
    {
    }

    protected NovelAgentDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<UserSettings> UserSettings { get; set; } = null!;
    public DbSet<NovelProject> NovelProjects { get; set; } = null!;
    public DbSet<Volume> Volumes { get; set; } = null!;
    public DbSet<Chapter> Chapters { get; set; } = null!;
    public DbSet<ChapterDraft> ChapterDrafts { get; set; } = null!;
    public DbSet<ChapterChange> ChapterChanges { get; set; } = null!;
    public DbSet<GenerationGateReportRecord> GenerationGateReports { get; set; } = null!;
    public DbSet<AgentReviewRecord> AgentReviews { get; set; } = null!;
    public DbSet<Foreshadow> Foreshadows { get; set; } = null!;
    public DbSet<Character> Characters { get; set; } = null!;
    public DbSet<Material> Materials { get; set; } = null!;
    public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;
    public DbSet<KnowledgeClassification> KnowledgeClassifications { get; set; } = null!;
    public DbSet<KnowledgeConflictReport> KnowledgeConflictReports { get; set; } = null!;
    public DbSet<KnowledgeDirectory> KnowledgeDirectories { get; set; } = null!;
    public DbSet<ContentDocument> ContentDocuments { get; set; } = null!;
    public DbSet<ContentChunk> ContentChunks { get; set; } = null!;
    public DbSet<ContentVectorPoint> ContentVectorPoints { get; set; } = null!;
    public DbSet<ChapterVersion> ChapterVersions { get; set; } = null!;
    public DbSet<TianmingPackage> TianmingPackages { get; set; } = null!;
    public DbSet<ProductionEvent> ProductionEvents { get; set; } = null!;
    public DbSet<ProjectFactSnapshot> ProjectFactSnapshots { get; set; } = null!;
    public DbSet<CreativeIntent> CreativeIntents { get; set; } = null!;
    public DbSet<RevisionPlan> RevisionPlans { get; set; } = null!;
    public DbSet<OutboxEvent> OutboxEvents { get; set; } = null!;
    public DbSet<AgentMemory> AgentMemories { get; set; } = null!;
    public DbSet<AgentChatTurn> AgentChatTurns { get; set; } = null!;
    public DbSet<AgentChatSummary> AgentChatSummaries { get; set; } = null!;
    public DbSet<AgentMemoryEvent> AgentMemoryEvents { get; set; } = null!;
    public DbSet<AgentMemoryVersion> AgentMemoryVersions { get; set; } = null!;
    public DbSet<AgentMemoryRead> AgentMemoryReads { get; set; } = null!;
    public DbSet<AgentMemoryPromotion> AgentMemoryPromotions { get; set; } = null!;
    public DbSet<ProjectKnowledgeUsage> ProjectKnowledgeUsages { get; set; } = null!;
    public DbSet<AgentSession> AgentSessions { get; set; } = null!;
    public DbSet<StoryConstitution> StoryConstitutions { get; set; } = null!;
    public DbSet<VolumeArc> VolumeArcs { get; set; } = null!;
    public DbSet<ForeshadowEntry> ForeshadowEntries { get; set; } = null!;
    public DbSet<WorldSettingEntry> WorldSettingEntries { get; set; } = null!;
    public DbSet<AgentRun> AgentRuns { get; set; } = null!;
    public DbSet<AgentToolExecution> AgentToolExecutions { get; set; } = null!;
    public DbSet<AgentToolSearchSnapshot> AgentToolSearchSnapshots { get; set; } = null!;
    public DbSet<AgentRuntimeRun> AgentRuntimeRuns { get; set; } = null!;
    public DbSet<AgentInterrupt> AgentInterrupts { get; set; } = null!;
    public DbSet<AgentRuntimeEvent> AgentRuntimeEvents { get; set; } = null!;
    public DbSet<KnowledgeProcessingTask> KnowledgeProcessingTasks { get; set; } = null!;
    public DbSet<ProjectDesignRule> ProjectDesignRules { get; set; } = null!;
    public DbSet<ChapterBlueprint> ChapterBlueprints { get; set; } = null!;
    public DbSet<CreativeGoal> CreativeGoals { get; set; } = null!;
    public DbSet<GoalRevision> GoalRevisions { get; set; } = null!;
    public DbSet<TaskGraphVersion> TaskGraphVersions { get; set; } = null!;
    public DbSet<KernelTask> KernelTasks { get; set; } = null!;
    public DbSet<KernelArtifact> KernelArtifacts { get; set; } = null!;
    public DbSet<DomainEvent> DomainEvents { get; set; } = null!;
    public DbSet<ModelExecution> ModelExecutions { get; set; } = null!;
    public DbSet<CanonBranch> CanonBranches { get; set; } = null!;
    public DbSet<CandidateChapter> CandidateChapters { get; set; } = null!;
    public DbSet<CandidateAcceptance> CandidateAcceptances { get; set; } = null!;
    public DbSet<BranchMergeRecord> BranchMergeRecords { get; set; } = null!;
    public DbSet<ContinuitySummary> ContinuitySummaries { get; set; } = null!;
    public DbSet<CanonChange> CanonChanges { get; set; } = null!;
    public DbSet<GoalContextSnapshot> GoalContextSnapshots { get; set; } = null!;
    public DbSet<ModelKernelConfiguration> ModelKernelConfigurations { get; set; } = null!;
    public DbSet<ReworkIntent> ReworkIntents { get; set; } = null!;
    public DbSet<KnowledgeDocumentBlob> KnowledgeDocumentBlobs { get; set; } = null!;
    public DbSet<KnowledgeSection> KnowledgeSections { get; set; } = null!;
    public DbSet<KnowledgeChunk> KnowledgeChunks { get; set; } = null!;
    public DbSet<KnowledgeEntry> KnowledgeEntries { get; set; } = null!;
    public DbSet<StyleProfile> StyleProfiles { get; set; } = null!;
    public DbSet<KnowledgeCitation> KnowledgeCitations { get; set; } = null!;
    public DbSet<VectorIndexRecord> VectorIndexRecords { get; set; } = null!;
    public DbSet<AuthorMemory> AuthorMemories { get; set; } = null!;
    public DbSet<ProjectCollaborationDecision> ProjectCollaborationDecisions { get; set; } = null!;
    public DbSet<SessionDialogueState> SessionDialogueStates { get; set; } = null!;
    public DbSet<ExperienceObservation> ExperienceObservations { get; set; } = null!;
    public DbSet<ExperienceSuggestion> ExperienceSuggestions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore non-entity types from Support namespace
        modelBuilder.Ignore<TM.Web.NovelAgentWeb.Support.ChatMessage>();
        modelBuilder.Ignore<TM.Web.NovelAgentWeb.Support.ChatSummary>();
        modelBuilder.Ignore<TM.Web.NovelAgentWeb.Support.LayeredChatHistory>();

        modelBuilder.ConfigureTargetArchitecture();

        // User entity configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username").IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").IsRequired();
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.Property(e => e.StorageQuotaMb).HasColumnName("storage_quota_mb").HasDefaultValue(5120);
            entity.Property(e => e.ApiCallQuota).HasColumnName("api_call_quota").HasDefaultValue(10000);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);

            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // UserSettings entity configuration
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.ToTable("user_settings");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.LlmProvider).HasColumnName("llm_provider");
            entity.Property(e => e.LlmApiKeyEncrypted).HasColumnName("llm_api_key_encrypted");
            entity.Property(e => e.LlmBaseUrl).HasColumnName("llm_base_url");
            entity.Property(e => e.LlmModel).HasColumnName("llm_model");
            entity.Property(e => e.LlmTemperature).HasColumnName("llm_temperature").HasDefaultValue(0.7f);
            entity.Property(e => e.LlmMaxTokens).HasColumnName("llm_max_tokens").HasDefaultValue(4096);
            entity.Property(e => e.EmbeddingProvider).HasColumnName("embedding_provider").HasDefaultValue("local");
            entity.Property(e => e.EmbeddingModel).HasColumnName("embedding_model").HasDefaultValue("bge-small-zh-v1.5");
            entity.Property(e => e.AgentDefaultRisk).HasColumnName("agent_default_risk").HasDefaultValue("Medium");
            entity.Property(e => e.AgentLoopAutoProceed).HasColumnName("agent_loop_auto_proceed").HasDefaultValue(true);
            entity.Property(e => e.AgentLoopMaxSteps).HasColumnName("agent_loop_max_steps").HasDefaultValue(12);
            entity.Property(e => e.DefaultGenre).HasColumnName("default_genre").HasDefaultValue("玄幻");
            entity.Property(e => e.DefaultChapterWordCount).HasColumnName("default_chapter_word_count").HasDefaultValue(3000);
            entity.Property(e => e.Theme).HasColumnName("theme").HasDefaultValue("dark");
            entity.Property(e => e.Language).HasColumnName("language").HasDefaultValue("zh-CN");

            entity.HasOne(e => e.User)
                .WithOne(u => u.UserSettings)
                .HasForeignKey<UserSettings>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NovelProject entity configuration
        modelBuilder.Entity<NovelProject>(entity =>
        {
            entity.ToTable("novel_projects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Genre).HasColumnName("genre");
            entity.Property(e => e.SubGenre).HasColumnName("sub_genre");
            entity.Property(e => e.CoreHook).HasColumnName("core_hook");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("draft");
            entity.Property(e => e.WordCount).HasColumnName("word_count").HasDefaultValue(0);
            entity.Property(e => e.CoverImageUrl).HasColumnName("cover_image_url");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_novel_projects_idempotency");

            entity.HasOne(e => e.User)
                .WithMany(u => u.NovelProjects)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Volume entity configuration
        modelBuilder.Entity<Volume>(entity =>
        {
            entity.ToTable("volumes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.VolumeNumber).HasColumnName("volume_number");
            entity.Property(e => e.Summary).HasColumnName("summary");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Volumes)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Chapter entity configuration
        modelBuilder.Entity<Chapter>(entity =>
        {
            entity.ToTable("chapters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.VolumeId).HasColumnName("volume_id");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.ChapterNumber).HasColumnName("chapter_number");
            entity.Property(e => e.WordCount).HasColumnName("word_count").HasDefaultValue(0);
            entity.Property(e => e.CurrentDocumentId).HasColumnName("current_document_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("draft");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_chapters_project");
            entity.HasIndex(e => e.VolumeId).HasDatabaseName("idx_chapters_volume");
            entity.HasIndex(e => e.CurrentDocumentId).HasDatabaseName("idx_chapters_current_document");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_chapters_status");
            entity.HasIndex(e => new { e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_chapters_idempotency");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Chapters)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Volume)
                .WithMany(v => v.Chapters)
                .HasForeignKey(e => e.VolumeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<ContentDocument>()
                .WithMany()
                .HasForeignKey(e => e.CurrentDocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Foreshadow entity configuration
        modelBuilder.Entity<Foreshadow>(entity =>
        {
            entity.ToTable("foreshadows");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("planned");
            entity.Property(e => e.SetupChapterId).HasColumnName("setup_chapter_id");
            entity.Property(e => e.PayoffChapterId).HasColumnName("payoff_chapter_id");
            entity.Property(e => e.Importance).HasColumnName("importance");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_foreshadows_project");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_foreshadows_status");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Foreshadows)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SetupChapter)
                .WithMany(c => c.ForeshadowsSetup)
                .HasForeignKey(e => e.SetupChapterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PayoffChapter)
                .WithMany(c => c.ForeshadowsPayoff)
                .HasForeignKey(e => e.PayoffChapterId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Character entity configuration
        modelBuilder.Entity<Character>(entity =>
        {
            entity.ToTable("characters");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.Name).HasColumnName("name").IsRequired();
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.Property(e => e.Alias).HasColumnName("alias");
            entity.Property(e => e.Age).HasColumnName("age");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.Appearance).HasColumnName("appearance");
            entity.Property(e => e.Personality).HasColumnName("personality");
            entity.Property(e => e.Background).HasColumnName("background");
            entity.Property(e => e.InitialPowerLevel).HasColumnName("initial_power_level");
            entity.Property(e => e.CurrentPowerLevel).HasColumnName("current_power_level");
            entity.Property(e => e.SpecialAbilities).HasColumnName("special_abilities");
            entity.Property(e => e.CoreGoal).HasColumnName("core_goal");
            entity.Property(e => e.Motivation).HasColumnName("motivation");
            entity.Property(e => e.Relationships).HasColumnName("relationships");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("active");
            entity.Property(e => e.FirstAppearChapter).HasColumnName("first_appear_chapter");
            entity.Property(e => e.LastAppearChapter).HasColumnName("last_appear_chapter");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_characters_project");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_characters_idempotency");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Characters)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Chapter>()
                .WithMany()
                .HasForeignKey(e => e.FirstAppearChapter)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Chapter>()
                .WithMany()
                .HasForeignKey(e => e.LastAppearChapter)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Material entity configuration
        modelBuilder.Entity<Material>(entity =>
        {
            entity.ToTable("materials");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.Tags).HasColumnName("tags");
            entity.Property(e => e.RawDocumentId).HasColumnName("raw_document_id");
            entity.Property(e => e.VectorChunkCount).HasColumnName("vector_chunk_count").HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_materials_user");
            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_materials_project");
            entity.HasIndex(e => new { e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_materials_idempotency");
            entity.HasIndex(e => e.RawDocumentId).HasDatabaseName("idx_materials_raw_document");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Materials)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Materials)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ContentDocument>()
                .WithMany()
                .HasForeignKey(e => e.RawDocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // KnowledgeBase entity configuration
        modelBuilder.Entity<KnowledgeBase>(entity =>
        {
            entity.ToTable("knowledge_base");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.SourceProjectId).HasColumnName("project_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
            entity.Property(e => e.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
            entity.Property(e => e.VectorId).HasColumnName("vector_id").HasMaxLength(100);
            entity.Property(e => e.SourceType).HasColumnName("source_type").HasMaxLength(50).HasDefaultValue("manual");
            entity.Property(e => e.SourceUploadTaskId).HasColumnName("source_upload_task_id").HasMaxLength(100);
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.ExtractionContext).HasColumnName("extraction_context");
            entity.Property(e => e.Tags).HasColumnName("tags");
            entity.Property(e => e.Weight).HasColumnName("weight").HasDefaultValue(5);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_knowledge_base_user");
            entity.HasIndex(e => new { e.UserId, e.IsArchived }).HasDatabaseName("idx_knowledge_base_user_archived");
            entity.HasIndex(e => e.SourceProjectId).HasDatabaseName("idx_knowledge_base_source_project");
            entity.HasIndex(e => new { e.UserId, e.SourceProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_knowledge_base_idempotency");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceProject)
                .WithMany()
                .HasForeignKey(e => e.SourceProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AgentMemory entity configuration
        modelBuilder.Entity<AgentMemory>(entity =>
        {
            entity.ToTable("agent_memories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.MemoryType).HasColumnName("memory_type").IsRequired();
            entity.Property(e => e.MemoryKey).HasColumnName("memory_key").HasDefaultValue(string.Empty);
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.ProjectId }).HasDatabaseName("idx_memories_user_project");
            entity.HasIndex(e => new { e.UserId, e.MemoryType })
                .HasDatabaseName("IX_agent_memories_user_type_global")
                .IsUnique()
                .HasFilter("project_id IS NULL AND session_id IS NULL");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.MemoryType })
                .HasDatabaseName("IX_agent_memories_user_project_type")
                .IsUnique()
                .HasFilter("project_id IS NOT NULL AND session_id IS NULL");
            entity.HasIndex(e => new { e.UserId, e.SessionId, e.MemoryType })
                .HasDatabaseName("IX_agent_memories_user_session_type")
                .IsUnique()
                .HasFilter("session_id IS NOT NULL");

            entity.HasOne(e => e.User)
                .WithMany(u => u.AgentMemories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AgentMemories)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // KnowledgeDirectory entity configuration
        modelBuilder.Entity<KnowledgeDirectory>(entity =>
        {
            entity.ToTable("knowledge_directories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.Key).HasColumnName("directory_key").HasMaxLength(120).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.Key })
                .IsUnique()
                .HasDatabaseName("idx_knowledge_directories_user_key");
            entity.HasIndex(e => new { e.UserId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_knowledge_directories_idempotency");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ContentDocument entity configuration
        modelBuilder.Entity<ContentDocument>(entity =>
        {
            entity.ToTable("content_documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SourceType).HasColumnName("source_type").IsRequired();
            entity.Property(e => e.SourceId).HasColumnName("source_id").IsRequired();
            entity.Property(e => e.DocumentRole).HasColumnName("document_role").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.MimeType).HasColumnName("mime_type").HasDefaultValue("text/plain");
            entity.Property(e => e.ContentHash).HasColumnName("content_hash").IsRequired();
            entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => new { e.SourceType, e.SourceId, e.Version });

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ContentChunk entity configuration
        modelBuilder.Entity<ContentChunk>(entity =>
        {
            entity.ToTable("content_chunks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.ChunkText).HasColumnName("chunk_text").IsRequired();
            entity.Property(e => e.TokenCount).HasColumnName("token_count");
            entity.Property(e => e.CharStart).HasColumnName("char_start");
            entity.Property(e => e.CharEnd).HasColumnName("char_end");
            entity.Property(e => e.ContentHash).HasColumnName("content_hash").IsRequired();

            entity.HasAlternateKey(e => new { e.Id, e.DocumentId });
            entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex }).IsUnique();

            entity.HasOne(e => e.Document)
                .WithMany(d => d.Chunks)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ContentVectorPoint entity configuration
        modelBuilder.Entity<ContentVectorPoint>(entity =>
        {
            entity.ToTable("content_vector_points");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.QdrantCollection).HasColumnName("qdrant_collection").IsRequired();
            entity.Property(e => e.QdrantPointId).HasColumnName("qdrant_point_id").IsRequired();
            entity.Property(e => e.VectorModel).HasColumnName("vector_model").IsRequired();
            entity.Property(e => e.IndexedAt).HasColumnName("indexed_at");
            entity.Property(e => e.IndexStatus).HasColumnName("index_status").HasDefaultValue("pending");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");

            entity.HasIndex(e => e.DocumentId);
            entity.HasIndex(e => e.ChunkId);
            entity.HasIndex(e => new { e.ChunkId, e.DocumentId });
            entity.HasIndex(e => new { e.QdrantCollection, e.QdrantPointId }).IsUnique();

            entity.HasOne(e => e.Document)
                .WithMany()
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ContentChunk>()
                .WithMany()
                .HasForeignKey(e => new { e.ChunkId, e.DocumentId })
                .HasPrincipalKey(e => new { e.Id, e.DocumentId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChapterVersion>(entity =>
        {
            entity.ToTable("chapter_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id").IsRequired();
            entity.Property(e => e.ContentDocumentId).HasColumnName("content_document_id").IsRequired();
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.WordCount).HasColumnName("word_count");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("draft");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id");
            entity.Property(e => e.PackageId).HasColumnName("package_id");
            entity.Property(e => e.GateReportJson).HasColumnName("gate_report_json");
            entity.Property(e => e.AgentReviewJson).HasColumnName("agent_review_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.ChapterId, e.VersionNumber })
                .IsUnique()
                .HasDatabaseName("idx_chapter_versions_chapter_version");
            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_chapter_versions_project_chapter_created");
            entity.HasIndex(e => e.ContentDocumentId)
                .HasDatabaseName("idx_chapter_versions_document");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Chapter)
                .WithMany(c => c.Versions)
                .HasForeignKey(e => e.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ContentDocument)
                .WithMany()
                .HasForeignKey(e => e.ContentDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TianmingPackage>(entity =>
        {
            entity.ToTable("tianming_packages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id");
            entity.Property(e => e.PackageKind).HasColumnName("package_kind").HasDefaultValue("chapter_generation");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            entity.Property(e => e.InputJson).HasColumnName("input_json").IsRequired();
            entity.Property(e => e.DependencyVersionsJson).HasColumnName("dependency_versions_json");
            entity.Property(e => e.KnowledgeSnapshotJson).HasColumnName("knowledge_snapshot_json");
            entity.Property(e => e.FactSnapshotJson).HasColumnName("fact_snapshot_json");
            entity.Property(e => e.PromptVersion).HasColumnName("prompt_version");
            entity.Property(e => e.KernelVersion).HasColumnName("kernel_version");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Ignore(e => e.Chapter);

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_tianming_packages_project_chapter");
            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_tianming_packages_run_created");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

        });

        modelBuilder.Entity<ProductionEvent>(entity =>
        {
            entity.ToTable("production_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id");
            entity.Property(e => e.PackageId).HasColumnName("package_id");
            entity.Property(e => e.EventType).HasColumnName("event_type").IsRequired();
            entity.Property(e => e.Stage).HasColumnName("stage").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Message).HasColumnName("message").IsRequired();
            entity.Property(e => e.ArtifactType).HasColumnName("artifact_type");
            entity.Property(e => e.ArtifactId).HasColumnName("artifact_id");
            entity.Property(e => e.DataJson).HasColumnName("data_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Ignore(e => e.Chapter);

            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_production_events_run_created");
            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_production_events_project_chapter_created");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

        });

        modelBuilder.Entity<ChapterChange>(entity =>
        {
            entity.ToTable("chapter_changes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("package_id");
            entity.Property(e => e.ChangesJson).HasColumnName("changes_json").IsRequired();
            entity.Property(e => e.CanonicalChangesJson).HasColumnName("canonical_changes_json").IsRequired();
            entity.Property(e => e.ParseStatus).HasColumnName("parse_status").HasDefaultValue("unknown");
            entity.Property(e => e.ParseError).HasColumnName("parse_error");
            entity.Property(e => e.AppliedToFactSnapshot).HasColumnName("applied_to_fact_snapshot").HasDefaultValue(false);
            entity.Property(e => e.AppliedAt).HasColumnName("applied_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Ignore(e => e.Chapter);

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_chapter_changes_project_chapter_created");
            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_chapter_changes_run_created");
            entity.HasIndex(e => new { e.ProjectId, e.ParseStatus })
                .HasDatabaseName("idx_chapter_changes_project_parse_status");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChapterDraft>(entity =>
        {
            entity.ToTable("chapter_drafts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("package_id");
            entity.Property(e => e.ArtifactId).HasColumnName("artifact_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("draft_generated");
            entity.Property(e => e.DraftContent).HasColumnName("draft_content").IsRequired();
            entity.Property(e => e.ChangesJson).HasColumnName("changes_json");
            entity.Property(e => e.ContentLength).HasColumnName("content_length");
            entity.Property(e => e.RepairAttemptCount).HasColumnName("repair_attempt_count");
            entity.Property(e => e.HasChanges).HasColumnName("has_changes");
            entity.Property(e => e.GeneratedAt).HasColumnName("generated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Ignore(e => e.Chapter);

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_chapter_drafts_project_chapter_created");
            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_chapter_drafts_run_created");
            entity.HasIndex(e => new { e.ProjectId, e.Status })
                .HasDatabaseName("idx_chapter_drafts_project_status");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GenerationGateReportRecord>(entity =>
        {
            entity.ToTable("generation_gate_reports");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("package_id");
            entity.Property(e => e.ArtifactId).HasColumnName("artifact_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            entity.Property(e => e.ReportJson).HasColumnName("report_json").IsRequired();
            entity.Property(e => e.ProtocolPassed).HasColumnName("protocol_passed");
            entity.Property(e => e.ChangesDetected).HasColumnName("changes_detected");
            entity.Property(e => e.FactSnapshotPassed).HasColumnName("fact_snapshot_passed");
            entity.Property(e => e.BlueprintPassed).HasColumnName("blueprint_passed");
            entity.Property(e => e.RagPassed).HasColumnName("rag_passed");
            entity.Property(e => e.IssueCount).HasColumnName("issue_count");
            entity.Property(e => e.RepairHintCount).HasColumnName("repair_hint_count");
            entity.Property(e => e.ValidatedAt).HasColumnName("validated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Ignore(e => e.Chapter);

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_generation_gate_reports_project_chapter_created");
            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_generation_gate_reports_run_created");
            entity.HasIndex(e => new { e.ProjectId, e.Status })
                .HasDatabaseName("idx_generation_gate_reports_project_status");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentReviewRecord>(entity =>
        {
            entity.ToTable("agent_reviews");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id").IsRequired();
            entity.Property(e => e.PackageId).HasColumnName("package_id");
            entity.Property(e => e.ReviewId).HasColumnName("review_id").IsRequired();
            entity.Property(e => e.OverallResult).HasColumnName("overall_result").HasDefaultValue("Unknown");
            entity.Property(e => e.ValidationOverallResult).HasColumnName("validation_overall_result");
            entity.Property(e => e.RequiresRewrite).HasColumnName("requires_rewrite");
            entity.Property(e => e.QualityScore).HasColumnName("quality_score");
            entity.Property(e => e.ContentLength).HasColumnName("content_length");
            entity.Property(e => e.CheckCount).HasColumnName("check_count");
            entity.Property(e => e.Summary).HasColumnName("summary");
            entity.Property(e => e.MeetsAcceptedCreativeIntents).HasColumnName("meets_accepted_creative_intents").HasDefaultValue(true);
            entity.Property(e => e.ContinuityRisk).HasColumnName("continuity_risk").HasDefaultValue(string.Empty);
            entity.Property(e => e.ChapterPacing).HasColumnName("chapter_pacing").HasDefaultValue(string.Empty);
            entity.Property(e => e.RecommendedAction).HasColumnName("recommended_action").HasDefaultValue(string.Empty);
            entity.Property(e => e.ReviewJson).HasColumnName("review_json").IsRequired();
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Ignore(e => e.Chapter);

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.CreatedAt })
                .HasDatabaseName("idx_agent_reviews_project_chapter_created");
            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_agent_reviews_run_created");
            entity.HasIndex(e => new { e.ProjectId, e.OverallResult })
                .HasDatabaseName("idx_agent_reviews_project_result");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectFactSnapshot>(entity =>
        {
            entity.ToTable("project_fact_snapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id");
            entity.Property(e => e.ChapterVersionId).HasColumnName("chapter_version_id");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.SnapshotJson).HasColumnName("snapshot_json").IsRequired();
            entity.Property(e => e.Source).HasColumnName("source").HasDefaultValue("unknown");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.VersionNumber })
                .HasDatabaseName("idx_fact_snapshots_project_chapter_version");
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt })
                .HasDatabaseName("idx_fact_snapshots_project_created");

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Chapter)
                .WithMany()
                .HasForeignKey(e => e.ChapterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ChapterVersion)
                .WithMany()
                .HasForeignKey(e => e.ChapterVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CreativeIntent>(entity =>
        {
            entity.ToTable("creative_intents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.Source).HasColumnName("source").HasDefaultValue("chat");
            entity.Property(e => e.RawContent).HasColumnName("raw_content").IsRequired();
            entity.Property(e => e.NormalizedIntent).HasColumnName("normalized_intent").IsRequired();
            entity.Property(e => e.TargetScope).HasColumnName("target_scope").HasDefaultValue("project");
            entity.Property(e => e.TargetVolumeId).HasColumnName("target_volume_id");
            entity.Property(e => e.TargetChapterId).HasColumnName("target_chapter_id");
            entity.Property(e => e.TargetCharacterName).HasColumnName("target_character_name");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("candidate");
            entity.Property(e => e.ImpactLevel).HasColumnName("impact_level").HasDefaultValue("future_carry");
            entity.Property(e => e.RequiresConfirmation).HasColumnName("requires_confirmation").HasDefaultValue(false);
            entity.Property(e => e.ConflictStatus).HasColumnName("conflict_status").HasDefaultValue("unknown");
            entity.Property(e => e.DecisionReason).HasColumnName("decision_reason");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json").HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entity.Property(e => e.ExecutedAt).HasColumnName("executed_at");

            entity.HasIndex(e => new { e.ProjectId, e.Status, e.CreatedAt })
                .HasDatabaseName("idx_creative_intents_project_status_created");
            entity.HasIndex(e => new { e.ProjectId, e.TargetChapterId, e.Status })
                .HasDatabaseName("idx_creative_intents_project_chapter_status");
            entity.HasIndex(e => new { e.SessionId, e.CreatedAt })
                .HasDatabaseName("idx_creative_intents_session_created");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_creative_intents_idempotency");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RevisionPlan>(entity =>
        {
            entity.ToTable("revision_plans");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.CreativeIntentId).HasColumnName("creative_intent_id");
            entity.Property(e => e.KnowledgeConflictReportId).HasColumnName("knowledge_conflict_report_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.Source).HasColumnName("source").HasDefaultValue("creative_intent");
            entity.Property(e => e.PlanType).HasColumnName("plan_type").HasDefaultValue("future_carry");
            entity.Property(e => e.TargetScope).HasColumnName("target_scope").HasDefaultValue("project");
            entity.Property(e => e.TargetVolumeId).HasColumnName("target_volume_id");
            entity.Property(e => e.TargetChapterId).HasColumnName("target_chapter_id");
            entity.Property(e => e.TargetChapterLogicalId).HasColumnName("target_chapter_logical_id");
            entity.Property(e => e.TargetChapterDisplayName).HasColumnName("target_chapter_display_name");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("draft");
            entity.Property(e => e.RequirementsJson).HasColumnName("requirements_json").HasDefaultValue("[]");
            entity.Property(e => e.ContinuityRequirementsJson).HasColumnName("continuity_requirements_json").HasDefaultValue("[]");
            entity.Property(e => e.ImpactAnalysisJson).HasColumnName("impact_analysis_json").HasDefaultValue("{}");
            entity.Property(e => e.AffectedChapterIdsJson).HasColumnName("affected_chapter_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.InvalidatedPackageIdsJson).HasColumnName("invalidated_package_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.RiskLevel).HasColumnName("risk_level").HasDefaultValue("medium");
            entity.Property(e => e.Recommendation).HasColumnName("recommendation");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.ProjectId, e.Status, e.CreatedAt })
                .HasDatabaseName("idx_revision_plans_project_status_created");
            entity.HasIndex(e => new { e.ProjectId, e.TargetChapterId, e.Status })
                .HasDatabaseName("idx_revision_plans_project_chapter_status");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_revision_plans_idempotency");
            entity.HasIndex(e => e.CreativeIntentId).HasDatabaseName("idx_revision_plans_creative_intent");
            entity.HasIndex(e => e.KnowledgeConflictReportId).HasDatabaseName("idx_revision_plans_conflict_report");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreativeIntent)
                .WithMany()
                .HasForeignKey(e => e.CreativeIntentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.KnowledgeConflictReport)
                .WithMany()
                .HasForeignKey(e => e.KnowledgeConflictReportId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id");
            entity.Property(e => e.EventType).HasColumnName("event_type").IsRequired();
            entity.Property(e => e.AggregateType).HasColumnName("aggregate_type").IsRequired();
            entity.Property(e => e.AggregateId).HasColumnName("aggregate_id").IsRequired();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasDefaultValue(string.Empty);
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            entity.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at");
            entity.Property(e => e.ProcessingOwner).HasColumnName("processing_owner");
            entity.Property(e => e.ProcessingLeaseExpiresAt).HasColumnName("processing_lease_expires_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt, e.CreatedAt })
                .HasDatabaseName("idx_outbox_status_retry");
            entity.HasIndex(e => new { e.Status, e.ProcessingLeaseExpiresAt })
                .HasDatabaseName("idx_outbox_processing_lease");
            entity.HasIndex(e => new { e.AggregateType, e.AggregateId })
                .HasDatabaseName("idx_outbox_aggregate");
            entity.HasIndex(e => new { e.UserId, e.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key <> ''")
                .HasDatabaseName("ux_outbox_idempotency");
        });

        // AgentChatTurn entity configuration
        modelBuilder.Entity<AgentChatTurn>(entity =>
        {
            entity.ToTable("agent_chat_turns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.TurnIndex).HasColumnName("turn_index");
            entity.Property(e => e.Role).HasColumnName("role").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.TokenCount).HasColumnName("token_count");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CompressedIntoSummaryId).HasColumnName("compressed_into_summary_id");

            entity.HasIndex(e => new { e.SessionId, e.TurnIndex }).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ProjectId);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<AgentSession>()
                .WithMany(s => s.ChatTurns)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentChatSummary entity configuration
        modelBuilder.Entity<AgentChatSummary>(entity =>
        {
            entity.ToTable("agent_chat_summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.StartTurn).HasColumnName("start_turn");
            entity.Property(e => e.EndTurn).HasColumnName("end_turn");
            entity.Property(e => e.SummaryType).HasColumnName("summary_type").HasDefaultValue("summary");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.KeyDecisionsJson).HasColumnName("key_decisions_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => new { e.UserId, e.SessionId, e.SummaryType, e.StartTurn, e.EndTurn })
                .IsUnique()
                .HasDatabaseName("IX_agent_chat_summaries_range_global")
                .HasFilter("project_id IS NULL");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.SessionId, e.SummaryType, e.StartTurn, e.EndTurn })
                .IsUnique()
                .HasDatabaseName("IX_agent_chat_summaries_range_project")
                .HasFilter("project_id IS NOT NULL");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<AgentSession>()
                .WithMany(s => s.ChatSummaries)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentMemoryEvent entity configuration
        modelBuilder.Entity<AgentMemoryEvent>(entity =>
        {
            entity.ToTable("agent_memory_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.SourceType).HasColumnName("source_type").IsRequired();
            entity.Property(e => e.TriggerType).HasColumnName("trigger_type").IsRequired();
            entity.Property(e => e.MemoryScope).HasColumnName("memory_scope").IsRequired();
            entity.Property(e => e.MemoryKey).HasColumnName("memory_key").IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.RunId);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentMemoryVersion entity configuration
        modelBuilder.Entity<AgentMemoryVersion>(entity =>
        {
            entity.ToTable("agent_memory_versions");
            entity.Property<long>("Id").HasColumnName("id").ValueGeneratedOnAdd();
            entity.HasKey("Id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.Scope).HasColumnName("scope").IsRequired();
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.SessionId, e.Scope }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.Scope })
                .HasDatabaseName("IX_agent_memory_versions_user_scope_global")
                .IsUnique()
                .HasFilter("project_id IS NULL AND session_id IS NULL");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.Scope })
                .HasDatabaseName("IX_agent_memory_versions_user_project_scope")
                .IsUnique()
                .HasFilter("project_id IS NOT NULL AND session_id IS NULL");
            entity.HasIndex(e => new { e.UserId, e.SessionId, e.Scope })
                .HasDatabaseName("IX_agent_memory_versions_user_session_scope")
                .IsUnique()
                .HasFilter("project_id IS NULL AND session_id IS NOT NULL");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.SessionId, e.Scope })
                .HasDatabaseName("IX_agent_memory_versions_user_project_session_scope_not_null")
                .IsUnique()
                .HasFilter("project_id IS NOT NULL AND session_id IS NOT NULL");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentMemoryRead entity configuration
        modelBuilder.Entity<AgentMemoryRead>(entity =>
        {
            entity.ToTable("agent_memory_reads");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.MemoryScope).HasColumnName("memory_scope").IsRequired();
            entity.Property(e => e.MemoryKeysJson).HasColumnName("memory_keys_json").HasDefaultValue("[]");
            entity.Property(e => e.SourceType).HasColumnName("source_type").HasDefaultValue("memory_repository");
            entity.Property(e => e.Consumer).HasColumnName("consumer").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }).HasDatabaseName("idx_agent_memory_reads_user_created");
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt }).HasDatabaseName("idx_agent_memory_reads_project_created");
            entity.HasIndex(e => e.SessionId).HasDatabaseName("idx_agent_memory_reads_session");
            entity.HasIndex(e => e.RunId).HasDatabaseName("idx_agent_memory_reads_run");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentMemoryPromotion entity configuration
        modelBuilder.Entity<AgentMemoryPromotion>(entity =>
        {
            entity.ToTable("agent_memory_promotions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.SourceScope).HasColumnName("source_scope").IsRequired();
            entity.Property(e => e.TargetScope).HasColumnName("target_scope").IsRequired();
            entity.Property(e => e.SourceMemoryKey).HasColumnName("source_memory_key").IsRequired();
            entity.Property(e => e.TargetMemoryKey).HasColumnName("target_memory_key").IsRequired();
            entity.Property(e => e.PromotionReason).HasColumnName("promotion_reason").IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json").HasDefaultValue("{}");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }).HasDatabaseName("idx_agent_memory_promotions_user_created");
            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt }).HasDatabaseName("idx_agent_memory_promotions_project_created");
            entity.HasIndex(e => e.SessionId).HasDatabaseName("idx_agent_memory_promotions_session");
            entity.HasIndex(e => e.RunId).HasDatabaseName("idx_agent_memory_promotions_run");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<NovelProject>()
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProjectKnowledgeUsage entity configuration
        modelBuilder.Entity<ProjectKnowledgeUsage>(entity =>
        {
            entity.ToTable("project_knowledge_usages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.KnowledgeId).HasColumnName("knowledge_id").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("imported");
            entity.Property(e => e.SourceSessionId).HasColumnName("source_session_id");
            entity.Property(e => e.SourceRunId).HasColumnName("source_run_id");
            entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.UsageCount).HasColumnName("usage_count");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Role).HasColumnName("role").HasDefaultValue("Reference");
            entity.Property(e => e.Scope).HasColumnName("scope").HasDefaultValue("ProjectWide");
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(50);
            entity.Property(e => e.ConstraintLevel).HasColumnName("constraint_level").HasDefaultValue("Reference");
            entity.Property(e => e.PackagePolicy).HasColumnName("package_policy").HasDefaultValue("RelevantOnly");
            entity.Property(e => e.BoundVersion).HasColumnName("bound_version");
            entity.Property(e => e.UsedByChaptersJson).HasColumnName("used_by_chapters_json");
            entity.Property(e => e.UsageIdempotencyKeysJson).HasColumnName("usage_idempotency_keys_json");

            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.KnowledgeId }).IsUnique();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<KnowledgeBase>()
                .WithMany()
                .HasForeignKey(e => e.KnowledgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeClassification>(entity =>
        {
            entity.ToTable("knowledge_classifications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.KnowledgeId).HasColumnName("knowledge_id").IsRequired();
            entity.Property(e => e.Model).HasColumnName("model").HasDefaultValue("");
            entity.Property(e => e.ClassificationJson).HasColumnName("classification_json").HasDefaultValue("{}");
            entity.Property(e => e.Role).HasColumnName("role").HasDefaultValue("");
            entity.Property(e => e.Scope).HasColumnName("scope").HasDefaultValue("");
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.ConstraintLevel).HasColumnName("constraint_level").HasDefaultValue("");
            entity.Property(e => e.PackagePolicy).HasColumnName("package_policy").HasDefaultValue("");
            entity.Property(e => e.Confidence).HasColumnName("confidence");
            entity.Property(e => e.SourceSessionId).HasColumnName("source_session_id");
            entity.Property(e => e.SourceRunId).HasColumnName("source_run_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.KnowledgeId, e.CreatedAt })
                .HasDatabaseName("idx_knowledge_classifications_project_knowledge");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Knowledge)
                .WithMany()
                .HasForeignKey(e => e.KnowledgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeConflictReport>(entity =>
        {
            entity.ToTable("knowledge_conflict_reports");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.KnowledgeId).HasColumnName("knowledge_id").IsRequired();
            entity.Property(e => e.ConflictingKnowledgeIdsJson).HasColumnName("conflicting_knowledge_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.ConflictType).HasColumnName("conflict_type").HasDefaultValue("");
            entity.Property(e => e.Severity).HasColumnName("severity").HasDefaultValue("");
            entity.Property(e => e.ImpactScope).HasColumnName("impact_scope").HasDefaultValue("");
            entity.Property(e => e.Explanation).HasColumnName("explanation").HasDefaultValue("");
            entity.Property(e => e.RecommendedAction).HasColumnName("recommended_action").HasDefaultValue("");
            entity.Property(e => e.RequiresUserDecision).HasColumnName("requires_user_decision");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("open");
            entity.Property(e => e.DetectionJson).HasColumnName("detection_json").HasDefaultValue("{}");
            entity.Property(e => e.SourceSessionId).HasColumnName("source_session_id");
            entity.Property(e => e.SourceRunId).HasColumnName("source_run_id");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(e => e.ResolutionNote).HasColumnName("resolution_note");
            entity.Property(e => e.ResolvedBySessionId).HasColumnName("resolved_by_session_id");
            entity.Property(e => e.ResolvedByRunId).HasColumnName("resolved_by_run_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.Status, e.CreatedAt })
                .HasDatabaseName("idx_knowledge_conflict_reports_project_status");
            entity.HasIndex(e => new { e.ProjectId, e.KnowledgeId })
                .HasDatabaseName("idx_knowledge_conflict_reports_knowledge");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Knowledge)
                .WithMany()
                .HasForeignKey(e => e.KnowledgeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentSession entity configuration
        modelBuilder.Entity<AgentSession>(entity =>
        {
            entity.ToTable("agent_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.Title).HasColumnName("title").IsRequired().HasDefaultValue("新会话");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
            entity.Property(e => e.SessionData).HasColumnName("session_data");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => new { e.UserId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_agent_sessions_idempotency");

            entity.HasOne(e => e.User)
                .WithMany(u => u.AgentSessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AgentSessions)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // StoryConstitution entity configuration
        modelBuilder.Entity<StoryConstitution>(entity =>
        {
            entity.ToTable("story_constitutions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.Genre).HasColumnName("genre").IsRequired();
            entity.Property(e => e.SubGenre).HasColumnName("sub_genre");
            entity.Property(e => e.CoreHook).HasColumnName("core_hook").IsRequired();
            entity.Property(e => e.ReaderPromise).HasColumnName("reader_promise");
            entity.Property(e => e.GenreProfile).HasColumnName("genre_profile");
            entity.Property(e => e.TargetAudience).HasColumnName("target_audience");
            entity.Property(e => e.Taboos).HasColumnName("taboos");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_story_constitutions_idempotency");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithOne(p => p.StoryConstitution)
                .HasForeignKey<StoryConstitution>(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // VolumeArc entity configuration
        modelBuilder.Entity<VolumeArc>(entity =>
        {
            entity.ToTable("volume_arcs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.VolumeNumber).HasColumnName("volume_number").IsRequired();
            entity.Property(e => e.VolumeTitle).HasColumnName("volume_title").IsRequired();
            entity.Property(e => e.VolumeTheme).HasColumnName("volume_theme");
            entity.Property(e => e.TargetChapters).HasColumnName("target_chapters");
            entity.Property(e => e.CurrentChapters).HasColumnName("current_chapters").HasDefaultValue(0);
            entity.Property(e => e.Act1Setup).HasColumnName("act1_setup");
            entity.Property(e => e.Act2Confrontation).HasColumnName("act2_confrontation");
            entity.Property(e => e.Act3Climax).HasColumnName("act3_climax");
            entity.Property(e => e.Act4Resolution).HasColumnName("act4_resolution");
            entity.Property(e => e.KeyEvents).HasColumnName("key_events").HasColumnType("text");
            entity.Property(e => e.MajorConflict).HasColumnName("major_conflict");
            entity.Property(e => e.ConflictEscalation).HasColumnName("conflict_escalation");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("planned");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");

            entity.HasIndex(e => new { e.ProjectId, e.VolumeNumber }).IsUnique();
            entity.HasIndex(e => new { e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_volume_arcs_idempotency");
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ForeshadowEntry entity configuration
        modelBuilder.Entity<ForeshadowEntry>(entity =>
        {
            entity.ToTable("foreshadow_ledger");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.Category).HasColumnName("category").IsRequired();
            entity.Property(e => e.PlantedInChapter).HasColumnName("planted_in_chapter").IsRequired();
            entity.Property(e => e.PlantedContext).HasColumnName("planted_context");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("planted");
            entity.Property(e => e.ResolvedInChapter).HasColumnName("resolved_in_chapter");
            entity.Property(e => e.ResolvedContext).HasColumnName("resolved_context");
            entity.Property(e => e.PlantedAt).HasColumnName("planted_at");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(5);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.ForeshadowEntries)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WorldSettingEntry entity configuration
        modelBuilder.Entity<WorldSettingEntry>(entity =>
        {
            entity.ToTable("world_settings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.Category).HasColumnName("category").IsRequired();
            entity.Property(e => e.SubCategory).HasColumnName("sub_category");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.FirstMentionedChapter).HasColumnName("first_mentioned_chapter");
            entity.Property(e => e.ReferencedChapters).HasColumnName("referenced_chapters");
            entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
            entity.Property(e => e.PreviousVersion).HasColumnName("previous_version");
            entity.Property(e => e.ChangeLog).HasColumnName("change_log");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Category);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.WorldSettingEntries)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentRun entity configuration
        modelBuilder.Entity<AgentRun>(entity =>
        {
            entity.ToTable("agent_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.RunType).HasColumnName("run_type").IsRequired();
            entity.Property(e => e.TargetChapterId).HasColumnName("target_chapter_id");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("running");
            entity.Property(e => e.InputParams).HasColumnName("input_params");
            entity.Property(e => e.OutputData).HasColumnName("output_data");
            entity.Property(e => e.OutputDocumentId).HasColumnName("output_document_id");
            entity.Property(e => e.ContextPackageSize).HasColumnName("context_package_size");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RunType);
            entity.HasIndex(e => e.OutputDocumentId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AgentRuns)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ContentDocument>()
                .WithMany()
                .HasForeignKey(e => e.OutputDocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AgentToolExecution>(entity =>
        {
            entity.ToTable("agent_tool_executions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.ToolName).HasColumnName("tool_name").IsRequired();
            entity.Property(e => e.Phase).HasColumnName("phase");
            entity.Property(e => e.Risk).HasColumnName("risk");
            entity.Property(e => e.ArgumentsJson).HasColumnName("arguments_json");
            entity.Property(e => e.ArgumentsHash).HasColumnName("arguments_hash").IsRequired();
            entity.Property(e => e.SideEffectsJson).HasColumnName("side_effects_json");
            entity.Property(e => e.SemanticContractJson).HasColumnName("semantic_contract_json");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.ResultPhase).HasColumnName("result_phase");
            entity.Property(e => e.ResultMessage).HasColumnName("result_message");
            entity.Property(e => e.ErrorType).HasColumnName("error_type");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.FailureJson).HasColumnName("failure_json");
            entity.Property(e => e.ArtifactJson).HasColumnName("artifact_json");
            entity.Property(e => e.RecommendedNextTool).HasColumnName("recommended_next_tool");
            entity.Property(e => e.MissingPrerequisite).HasColumnName("missing_prerequisite");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");

            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.SessionId, e.StartedAt })
                .HasDatabaseName("idx_agent_tool_executions_scope_recent");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.ToolName, e.ArgumentsHash })
                .HasDatabaseName("idx_agent_tool_executions_dedupe");
        });

        modelBuilder.Entity<AgentToolSearchSnapshot>(entity =>
        {
            entity.ToTable("agent_tool_search_snapshots");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.Phase).HasColumnName("phase").IsRequired();
            entity.Property(e => e.Version).HasColumnName("version").IsRequired();
            entity.Property(e => e.ToolsJson).HasColumnName("tools_json").IsRequired();
            entity.Property(e => e.SourceExecutionId).HasColumnName("source_execution_id");
            entity.Property(e => e.CachedAt).HasColumnName("cached_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");

            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.SessionId, e.Phase, e.Version })
                .IsUnique()
                .HasDatabaseName("idx_agent_tool_search_snapshots_scope_version");
            entity.HasIndex(e => e.ExpiresAt)
                .HasDatabaseName("idx_agent_tool_search_snapshots_expires_at");
        });

        modelBuilder.Entity<AgentRuntimeRun>(entity =>
        {
            entity.ToTable("agent_runtime_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.LockedProjectId).HasColumnName("locked_project_id");
            entity.Property(e => e.ExecutedToolsJson).HasColumnName("executed_tools_json").HasDefaultValue("[]");
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Mode).HasColumnName("mode").HasDefaultValue("inspect").IsRequired();
            entity.Property(e => e.CurrentPhase).HasColumnName("current_phase");
            entity.Property(e => e.CurrentStep).HasColumnName("current_step");
            entity.Property(e => e.ActiveTool).HasColumnName("active_tool");
            entity.Property(e => e.UserMessage).HasColumnName("user_message");
            entity.Property(e => e.SourceMessageId).HasColumnName("source_message_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.BudgetJson).HasColumnName("budget_json");
            entity.Property(e => e.LastMessage).HasColumnName("last_message");
            entity.Property(e => e.ResultJson).HasColumnName("result_json");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.FailureJson).HasColumnName("failure_json");
            entity.Property(e => e.CancelRequested).HasColumnName("cancel_requested");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.SessionId, e.Status, e.UpdatedAt })
                .HasDatabaseName("idx_agent_runtime_runs_session_status");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.Status, e.UpdatedAt })
                .HasDatabaseName("idx_agent_runtime_runs_project_status");
            entity.HasIndex(e => new { e.UserId, e.SessionId, e.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key <> ''")
                .HasDatabaseName("ux_agent_runtime_runs_idempotency");
            entity.HasIndex(e => new { e.UserId, e.SessionId })
                .IsUnique()
                .HasFilter("status IN ('queued', 'running')")
                .HasDatabaseName("ux_agent_runtime_runs_active_session");
        });

        modelBuilder.Entity<AgentInterrupt>(entity =>
        {
            entity.ToTable("agent_interrupts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.Kind).HasColumnName("kind").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").IsRequired();
            entity.Property(e => e.Priority).HasColumnName("priority");
            entity.Property(e => e.Message).HasColumnName("message").IsRequired();
            entity.Property(e => e.DecisionJson).HasColumnName("decision_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.ConsumedAt).HasColumnName("consumed_at");

            entity.HasIndex(e => new { e.RuntimeRunId, e.Status, e.Priority, e.CreatedAt })
                .HasDatabaseName("idx_agent_interrupts_run_pending");
            entity.HasIndex(e => new { e.UserId, e.SessionId, e.Status, e.CreatedAt })
                .HasDatabaseName("idx_agent_interrupts_session_pending");
        });

        modelBuilder.Entity<AgentRuntimeEvent>(entity =>
        {
            entity.ToTable("agent_runtime_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RuntimeRunId).HasColumnName("runtime_run_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.SessionId).HasColumnName("session_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.Type).HasColumnName("type").IsRequired();
            entity.Property(e => e.Stage).HasColumnName("stage");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.ArtifactType).HasColumnName("artifact_type");
            entity.Property(e => e.ArtifactId).HasColumnName("artifact_id");
            entity.Property(e => e.DisplaySurface).HasColumnName("display_surface").HasDefaultValue("chat");
            entity.Property(e => e.DisplayPolicy).HasColumnName("display_policy").HasDefaultValue("collapsible");
            entity.Property(e => e.Message).HasColumnName("message").IsRequired();
            entity.Property(e => e.DataJson).HasColumnName("data_json");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.SessionId, e.CreatedAt })
                .HasDatabaseName("idx_agent_runtime_events_session_recent");
            entity.HasIndex(e => new { e.RuntimeRunId, e.CreatedAt })
                .HasDatabaseName("idx_agent_runtime_events_run_recent");
        });

        // KnowledgeProcessingTask entity configuration
        modelBuilder.Entity<KnowledgeProcessingTask>(entity =>
        {
            entity.ToTable("knowledge_processing_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.FileSize).HasColumnName("file_size");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            entity.Property(e => e.ProcessingStage).HasColumnName("processing_stage").HasDefaultValue("extract");
            entity.Property(e => e.ProcessingOwner).HasColumnName("processing_owner");
            entity.Property(e => e.ProcessingLeaseExpiresAt).HasColumnName("processing_lease_expires_at");
            entity.Property(e => e.Attempt).HasColumnName("attempt").HasDefaultValue(0);
            entity.Property(e => e.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(3);
            entity.Property(e => e.Strategy).HasColumnName("strategy").HasDefaultValue("single_pass");
            entity.Property(e => e.Progress).HasColumnName("progress").HasDefaultValue(0);
            entity.Property(e => e.TotalChunks).HasColumnName("total_chunks");
            entity.Property(e => e.ProcessedChunks).HasColumnName("processed_chunks").HasDefaultValue(0);
            entity.Property(e => e.ExtractedEntriesCount).HasColumnName("extracted_entries_count").HasDefaultValue(0);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.UploadDocumentId).HasColumnName("upload_document_id");
            entity.Property(e => e.UploadBlobId).HasColumnName("upload_blob_id");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.ProcessingLeaseExpiresAt, e.CreatedAt })
                .HasDatabaseName("idx_knowledge_processing_tasks_claim");
            entity.HasIndex(e => e.UploadDocumentId);
            entity.HasIndex(e => e.UploadBlobId);
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("idx_knowledge_processing_tasks_idempotency");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ContentDocument>()
                .WithMany()
                .HasForeignKey(e => e.UploadDocumentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<KnowledgeDocumentBlob>()
                .WithMany()
                .HasForeignKey(e => e.UploadBlobId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ProjectDesignRule entity configuration
        modelBuilder.Entity<ProjectDesignRule>(entity =>
        {
            entity.ToTable("project_design_rules");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.RuleType).HasColumnName("rule_type").IsRequired();
            entity.Property(e => e.RuleContent).HasColumnName("rule_content").IsRequired();
            entity.Property(e => e.SourceKnowledgeIdsJson).HasColumnName("source_knowledge_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.ConstraintLevel).HasColumnName("constraint_level").HasDefaultValue("Reference");
            entity.Property(e => e.Scope).HasColumnName("scope").HasDefaultValue("ProjectWide");
            entity.Property(e => e.ScopeTarget).HasColumnName("scope_target");
            entity.Property(e => e.Priority).HasColumnName("priority").HasDefaultValue(50);
            entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("Active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.PreviousVersionId).HasColumnName("previous_version_id");

            entity.HasIndex(e => new { e.ProjectId, e.RuleType, e.Status })
                .HasDatabaseName("ix_project_design_rules_project_rule_type_status");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.CreatedAt })
                .HasDatabaseName("ix_project_design_rules_user_project_created");
            entity.HasIndex(e => new { e.ProjectId, e.RuleType, e.Version })
                .HasDatabaseName("ix_project_design_rules_version");
        });

        // ChapterBlueprint entity configuration
        modelBuilder.Entity<ChapterBlueprint>(entity =>
        {
            entity.ToTable("chapter_blueprints");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.VolumeId).HasColumnName("volume_id");
            entity.Property(e => e.ChapterId).HasColumnName("chapter_id").IsRequired();
            entity.Property(e => e.ChapterIndex).HasColumnName("chapter_index");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Intent).HasColumnName("intent").IsRequired();
            entity.Property(e => e.KeyEventsJson).HasColumnName("key_events_json").HasDefaultValue("[]");
            entity.Property(e => e.CharactersJson).HasColumnName("characters_json").HasDefaultValue("[]");
            entity.Property(e => e.ConflictNote).HasColumnName("conflict_note");
            entity.Property(e => e.EndingNote).HasColumnName("ending_note");
            entity.Property(e => e.RequiredKnowledgeIdsJson).HasColumnName("required_knowledge_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.AppliedDesignRuleIdsJson).HasColumnName("applied_design_rule_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.DependencyChapterIdsJson).HasColumnName("dependency_chapter_ids_json").HasDefaultValue("[]");
            entity.Property(e => e.ContextPackageId).HasColumnName("context_package_id");
            entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("Draft");
            entity.Property(e => e.TargetWordCount).HasColumnName("target_word_count");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.PreviousVersionId).HasColumnName("previous_version_id");

            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.Status })
                .HasDatabaseName("ix_chapter_blueprints_project_chapter_status");
            entity.HasIndex(e => new { e.UserId, e.ProjectId, e.CreatedAt })
                .HasDatabaseName("ix_chapter_blueprints_user_project_created");
            entity.HasIndex(e => new { e.ProjectId, e.ChapterId, e.Version })
                .HasDatabaseName("ix_chapter_blueprints_version");
            entity.HasIndex(e => new { e.ProjectId, e.ChapterIndex })
                .HasDatabaseName("ix_chapter_blueprints_chapter_index");
        });
    }
}
