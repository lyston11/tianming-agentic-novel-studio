using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Data;

public class NovelAgentDbContext : DbContext
{
    public NovelAgentDbContext(DbContextOptions<NovelAgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<UserSettings> UserSettings { get; set; } = null!;
    public DbSet<NovelProject> NovelProjects { get; set; } = null!;
    public DbSet<Volume> Volumes { get; set; } = null!;
    public DbSet<Chapter> Chapters { get; set; } = null!;
    public DbSet<Foreshadow> Foreshadows { get; set; } = null!;
    public DbSet<Character> Characters { get; set; } = null!;
    public DbSet<Material> Materials { get; set; } = null!;
    public DbSet<KnowledgeBase> KnowledgeBases { get; set; } = null!;
    public DbSet<AgentMemory> AgentMemories { get; set; } = null!;
    public DbSet<AgentSession> AgentSessions { get; set; } = null!;
    public DbSet<StoryConstitution> StoryConstitutions { get; set; } = null!;
    public DbSet<VolumeArc> VolumeArcs { get; set; } = null!;
    public DbSet<ForeshadowEntry> ForeshadowEntries { get; set; } = null!;
    public DbSet<WorldSettingEntry> WorldSettingEntries { get; set; } = null!;
    public DbSet<AgentRun> AgentRuns { get; set; } = null!;
    public DbSet<KnowledgeProcessingTask> KnowledgeProcessingTasks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Ignore non-entity types from Support namespace
        modelBuilder.Ignore<TM.Web.NovelAgentWeb.Support.ChatMessage>();
        modelBuilder.Ignore<TM.Web.NovelAgentWeb.Support.ChatSummary>();
        modelBuilder.Ignore<TM.Web.NovelAgentWeb.Support.LayeredChatHistory>();

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
            entity.Property(e => e.AgentAutoContinue).HasColumnName("agent_auto_continue").HasDefaultValue(true);
            entity.Property(e => e.AgentMaxAutoSteps).HasColumnName("agent_max_auto_steps").HasDefaultValue(12);
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
            entity.Property(e => e.StorageProjectName).HasColumnName("storage_project_name");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.StorageProjectName).IsUnique();

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
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("draft");
            entity.Property(e => e.ContentPath).HasColumnName("content_path").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_chapters_project");
            entity.HasIndex(e => e.VolumeId).HasDatabaseName("idx_chapters_volume");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_chapters_status");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Chapters)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Volume)
                .WithMany(v => v.Chapters)
                .HasForeignKey(e => e.VolumeId)
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

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Characters)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Material entity configuration
        modelBuilder.Entity<Material>(entity =>
        {
            entity.ToTable("materials");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.FilePath).HasColumnName("file_path");
            entity.Property(e => e.Tags).HasColumnName("tags");
            entity.Property(e => e.VectorChunkCount).HasColumnName("vector_chunk_count").HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_materials_user");
            entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_materials_project");

            entity.HasOne(e => e.User)
                .WithMany(u => u.Materials)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Materials)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // KnowledgeBase entity configuration
        modelBuilder.Entity<KnowledgeBase>(entity =>
        {
            entity.ToTable("knowledge_base");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").IsRequired();
            entity.Property(e => e.EntryType).HasColumnName("entry_type").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.UsageCount).HasColumnName("usage_count").HasDefaultValue(0);
            entity.Property(e => e.VectorId).HasColumnName("vector_id").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(e => e.Project)
                .WithMany(p => p.KnowledgeBases)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // AgentMemory entity configuration
        modelBuilder.Entity<AgentMemory>(entity =>
        {
            entity.ToTable("agent_memories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.MemoryType).HasColumnName("memory_type").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => new { e.UserId, e.ProjectId }).HasDatabaseName("idx_memories_user_project");

            entity.HasOne(e => e.User)
                .WithMany(u => u.AgentMemories)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AgentMemories)
                .HasForeignKey(e => e.ProjectId)
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
            entity.Property(e => e.Title).HasColumnName("title").IsRequired().HasDefaultValue("新会话");
            entity.Property(e => e.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
            entity.Property(e => e.SessionData).HasColumnName("session_data");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

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
            entity.Property(e => e.VolumeNumber).HasColumnName("volume_number").IsRequired();
            entity.Property(e => e.VolumeTitle).HasColumnName("volume_title").IsRequired();
            entity.Property(e => e.VolumeTheme).HasColumnName("volume_theme");
            entity.Property(e => e.TargetChapters).HasColumnName("target_chapters");
            entity.Property(e => e.CurrentChapters).HasColumnName("current_chapters").HasDefaultValue(0);
            entity.Property(e => e.Act1Setup).HasColumnName("act1_setup");
            entity.Property(e => e.Act2Confrontation).HasColumnName("act2_confrontation");
            entity.Property(e => e.Act3Climax).HasColumnName("act3_climax");
            entity.Property(e => e.Act4Resolution).HasColumnName("act4_resolution");
            entity.Property(e => e.KeyEvents).HasColumnName("key_events");
            entity.Property(e => e.MajorConflict).HasColumnName("major_conflict");
            entity.Property(e => e.ConflictEscalation).HasColumnName("conflict_escalation");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("planned");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");

            entity.HasIndex(e => new { e.ProjectId, e.VolumeNumber }).IsUnique();
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
            entity.Property(e => e.ContextPackageSize).HasColumnName("context_package_size");
            entity.Property(e => e.ContextPackagePath).HasColumnName("context_package_path");
            entity.Property(e => e.GateReportPath).HasColumnName("gate_report_path");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProjectId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.RunType);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany(p => p.AgentRuns)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // KnowledgeProcessingTask entity configuration
        modelBuilder.Entity<KnowledgeProcessingTask>(entity =>
        {
            entity.ToTable("knowledge_processing_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id");
            entity.Property(e => e.FileName).HasColumnName("file_name").IsRequired();
            entity.Property(e => e.FilePath).HasColumnName("file_path").IsRequired();
            entity.Property(e => e.FileSize).HasColumnName("file_size");
            entity.Property(e => e.Status).HasColumnName("status").HasDefaultValue("pending");
            entity.Property(e => e.Strategy).HasColumnName("strategy").HasDefaultValue("single_pass");
            entity.Property(e => e.Progress).HasColumnName("progress").HasDefaultValue(0);
            entity.Property(e => e.TotalChunks).HasColumnName("total_chunks");
            entity.Property(e => e.ProcessedChunks).HasColumnName("processed_chunks").HasDefaultValue(0);
            entity.Property(e => e.ExtractedEntriesCount).HasColumnName("extracted_entries_count").HasDefaultValue(0);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

