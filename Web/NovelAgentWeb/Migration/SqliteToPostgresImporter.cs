using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.DataMigration;

public sealed class SqliteToPostgresImporter
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredSchema =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["users"] = ["id", "username", "email", "password_hash", "role", "storage_quota_mb", "api_call_quota", "created_at", "last_login_at", "is_active"],
            ["user_settings"] = ["user_id", "llm_provider", "llm_api_key_encrypted", "llm_base_url", "llm_model", "llm_temperature", "llm_max_tokens", "embedding_provider", "embedding_model", "agent_default_risk", "agent_loop_auto_proceed", "agent_loop_max_steps", "default_genre", "default_chapter_word_count", "theme", "language"],
            ["novel_projects"] = ["id", "user_id", "title", "genre", "sub_genre", "core_hook", "status", "word_count", "cover_image_url", "created_at", "updated_at", "idempotency_key"],
            ["volumes"] = ["id", "project_id", "title", "volume_number", "summary"],
            ["chapters"] = ["id", "project_id", "volume_id", "title", "chapter_number", "word_count", "current_document_id", "status", "created_at", "updated_at", "idempotency_key"],
            ["content_documents"] = ["id", "user_id", "project_id", "source_type", "source_id", "document_role", "title", "mime_type", "content_hash", "version", "status", "created_at", "updated_at"],
            ["content_chunks"] = ["id", "document_id", "chunk_index", "chunk_text", "token_count", "char_start", "char_end", "content_hash"],
            ["chapter_versions"] = ["id", "user_id", "project_id", "chapter_id", "content_document_id", "version_number", "title", "word_count", "status", "runtime_run_id", "package_id", "gate_report_json", "agent_review_json", "created_at"],
            ["story_constitutions"] = ["id", "user_id", "project_id", "genre", "sub_genre", "core_hook", "reader_promise", "genre_profile", "target_audience", "taboos", "created_at", "updated_at", "idempotency_key"],
            ["characters"] = ["id", "user_id", "project_id", "name", "role", "alias", "age", "gender", "appearance", "personality", "background", "initial_power_level", "current_power_level", "special_abilities", "core_goal", "motivation", "relationships", "status", "first_appear_chapter", "last_appear_chapter", "created_at", "updated_at", "idempotency_key"],
            ["volume_arcs"] = ["id", "user_id", "project_id", "volume_number", "volume_title", "volume_theme", "target_chapters", "current_chapters", "act1_setup", "act2_confrontation", "act3_climax", "act4_resolution", "key_events", "major_conflict", "conflict_escalation", "status", "created_at", "updated_at", "completed_at", "idempotency_key"],
            ["world_settings"] = ["id", "user_id", "project_id", "category", "sub_category", "title", "content", "first_mentioned_chapter", "referenced_chapters", "version", "previous_version", "change_log", "created_at", "updated_at"],
            ["foreshadow_ledger"] = ["id", "user_id", "project_id", "title", "content", "category", "planted_in_chapter", "planted_context", "status", "resolved_in_chapter", "resolved_context", "planted_at", "resolved_at", "priority", "created_at", "updated_at"],
            ["knowledge_base"] = ["id", "user_id", "project_id", "entry_type", "title", "content", "usage_count", "created_at", "vector_id", "source_type", "source_upload_task_id", "chunk_index", "extraction_context", "tags", "weight", "is_archived", "idempotency_key"],
            ["project_knowledge_usages"] = ["id", "user_id", "project_id", "knowledge_id", "status", "source_session_id", "source_run_id", "first_seen_at", "last_used_at", "usage_count", "note", "role", "scope", "priority", "constraint_level", "package_policy", "bound_version", "used_by_chapters_json", "usage_idempotency_keys_json"],
            ["agent_memories"] = ["id", "user_id", "project_id", "session_id", "memory_type", "memory_key", "content", "created_at", "updated_at"],
            ["agent_runs"] = ["id", "user_id", "project_id", "run_type", "target_chapter_id", "status", "input_params", "output_data", "output_document_id", "context_package_size", "started_at", "completed_at", "duration_ms", "created_at", "updated_at"],
            ["agent_tool_executions"] = ["id", "user_id", "project_id", "session_id", "run_id", "tool_name", "phase", "risk", "arguments_json", "arguments_hash", "side_effects_json", "semantic_contract_json", "status", "result_phase", "result_message", "error_type", "error_message", "failure_json", "artifact_json", "recommended_next_tool", "missing_prerequisite", "started_at", "completed_at", "duration_ms"],
            ["agent_runtime_runs"] = ["id", "user_id", "session_id", "project_id", "locked_project_id", "executed_tools_json", "status", "mode", "current_phase", "current_step", "active_tool", "user_message", "source_message_id", "idempotency_key", "budget_json", "last_message", "result_json", "error_message", "failure_json", "cancel_requested", "started_at", "completed_at", "created_at", "updated_at"],
            ["agent_runtime_events"] = ["id", "runtime_run_id", "user_id", "session_id", "project_id", "type", "stage", "status", "artifact_type", "artifact_id", "display_surface", "display_policy", "message", "data_json", "created_at"]
        };

    private static readonly HashSet<string> TerminalRunStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "completed", "failed", "cancelled", "canceled" };
    private static readonly HashSet<string> TerminalToolStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "succeeded", "failed", "cancelled", "canceled" };
    private readonly NovelAgentDbContext _db;
    private readonly IBackgroundUserContext _backgroundUsers;

    public SqliteToPostgresImporter(NovelAgentDbContext db, IBackgroundUserContext backgroundUsers)
    {
        _db = db;
        _backgroundUsers = backgroundUsers;
    }

    public async Task<MigrationImportReport> ImportUserAsync(
        string sqlitePath,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(sqlitePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        };
        await using var source = new SqliteConnection(builder.ConnectionString);
        await source.OpenAsync(cancellationToken);
        return await ImportUserAsync(source, userId, cancellationToken);
    }

    public async Task<MigrationImportReport> ImportUserAsync(
        SqliteConnection source,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("迁移必须指定用户。", nameof(userId));
        if (source.State != System.Data.ConnectionState.Open)
            await source.OpenAsync(cancellationToken);
        await ValidateSchemaAsync(source, cancellationToken);

        using var userScope = _backgroundUsers.Push(userId);
        var tracker = new ImportTracker(userId);
        await ImportUserRowAsync(source, userId, tracker, cancellationToken);
        await ImportUserSettingsAsync(source, userId, tracker, cancellationToken);
        await ImportProjectsAsync(source, userId, tracker, cancellationToken);
        await ImportVolumesAsync(source, userId, tracker, cancellationToken);
        await ImportContentDocumentsAsync(source, userId, tracker, cancellationToken);
        await ImportContentChunksAsync(source, userId, tracker, cancellationToken);
        await ImportChaptersAsync(source, userId, tracker, cancellationToken);
        await ImportChapterVersionsAsync(source, userId, tracker, cancellationToken);
        await ImportStoryConstitutionsAsync(source, userId, tracker, cancellationToken);
        await ImportCharactersAsync(source, userId, tracker, cancellationToken);
        await ImportStoryBibleExtensionsAsync(source, userId, tracker, cancellationToken);
        await ImportKnowledgeAsync(source, userId, tracker, cancellationToken);
        await ImportKnowledgeUsagesAsync(source, userId, tracker, cancellationToken);
        await ImportMemoriesAsync(source, userId, tracker, cancellationToken);
        await ImportAgentRunsAsync(source, userId, tracker, cancellationToken);
        await ImportToolExecutionsAsync(source, userId, tracker, cancellationToken);
        await ImportRuntimeAuditAsync(source, userId, tracker, cancellationToken);
        return tracker.Report();
    }

    private async Task ImportUserSettingsAsync(
        SqliteConnection source,
        string userId,
        ImportTracker tracker,
        CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(source, """
            SELECT user_id, llm_provider, llm_base_url, llm_model, llm_temperature,
                   llm_max_tokens, embedding_provider, embedding_model, agent_default_risk,
                   agent_loop_auto_proceed, agent_loop_max_steps, default_genre,
                   default_chapter_word_count, theme, language
            FROM user_settings WHERE user_id = $user_id
            """, userId, reader => new UserSettings
        {
            UserId = Text(reader, "user_id"),
            LlmProvider = NullableText(reader, "llm_provider"),
            LlmApiKeyEncrypted = null,
            LlmBaseUrl = NullableText(reader, "llm_base_url"),
            LlmModel = NullableText(reader, "llm_model"),
            LlmTemperature = Real(reader, "llm_temperature"),
            LlmMaxTokens = Integer(reader, "llm_max_tokens"),
            EmbeddingProvider = Text(reader, "embedding_provider"),
            EmbeddingModel = Text(reader, "embedding_model"),
            AgentDefaultRisk = Text(reader, "agent_default_risk"),
            AgentLoopAutoProceed = Boolean(reader, "agent_loop_auto_proceed"),
            AgentLoopMaxSteps = Integer(reader, "agent_loop_max_steps"),
            DefaultGenre = Text(reader, "default_genre"),
            DefaultChapterWordCount = Integer(reader, "default_chapter_word_count"),
            Theme = Text(reader, "theme"),
            Language = Text(reader, "language")
        }, cancellationToken);
        await AddMissingAsync(rows, _db.UserSettings, item => item.UserId, "user_settings", tracker, cancellationToken);
    }

    public static async Task<IReadOnlyList<string>> ListSourceUsersAsync(
        string sqlitePath,
        CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(sqlitePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private
        };
        await using var source = new SqliteConnection(builder.ConnectionString);
        await source.OpenAsync(cancellationToken);
        await ValidateSchemaAsync(source, cancellationToken);
        return await ReadAsync(
            source,
            "SELECT id FROM users ORDER BY id",
            null,
            reader => Text(reader, "id"),
            cancellationToken);
    }

    private async Task ImportUserRowAsync(
        SqliteConnection source,
        string userId,
        ImportTracker tracker,
        CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(source, """
            SELECT id, username, email, password_hash, role, storage_quota_mb,
                   api_call_quota, created_at, last_login_at, is_active
            FROM users WHERE id = $user_id
            """, userId, reader => new User
        {
            Id = Text(reader, "id"),
            Username = Text(reader, "username"),
            Email = Text(reader, "email"),
            PasswordHash = Text(reader, "password_hash"),
            Role = Text(reader, "role"),
            StorageQuotaMb = Integer(reader, "storage_quota_mb"),
            ApiCallQuota = Integer(reader, "api_call_quota"),
            CreatedAt = Timestamp(reader, "created_at"),
            LastLoginAt = NullableTimestamp(reader, "last_login_at"),
            IsActive = Boolean(reader, "is_active")
        }, cancellationToken);
        var user = rows.SingleOrDefault() ?? throw new KeyNotFoundException("SQLite 源库中不存在指定用户。");
        if (await _db.Users.AnyAsync(item => item.Id == user.Id, cancellationToken)) tracker.Reused("users");
        else { _db.Users.Add(user); tracker.Imported("users"); await _db.SaveChangesAsync(cancellationToken); }
    }

    private async Task ImportProjectsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, title, genre, sub_genre, core_hook, status, word_count,
                   cover_image_url, created_at, updated_at, idempotency_key
            FROM novel_projects WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new NovelProject
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), Title = Text(reader, "title"),
            Genre = NullableText(reader, "genre"), SubGenre = NullableText(reader, "sub_genre"),
            CoreHook = NullableText(reader, "core_hook"), Status = Text(reader, "status"),
            WordCount = Integer(reader, "word_count"), CoverImageUrl = NullableText(reader, "cover_image_url"),
            CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at"),
            IdempotencyKey = NullableText(reader, "idempotency_key")
        }, ct);
        await AddMissingAsync(rows, _db.NovelProjects, item => item.Id, "novel_projects", tracker, ct);
    }

    private async Task ImportVolumesAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT volume.id, volume.project_id, volume.title, volume.volume_number, volume.summary
            FROM volumes AS volume
            JOIN novel_projects AS project ON project.id = volume.project_id
            WHERE project.user_id = $user_id ORDER BY volume.project_id, volume.volume_number
            """, userId, reader => new Volume
        {
            Id = Text(reader, "id"), ProjectId = Text(reader, "project_id"), Title = Text(reader, "title"),
            VolumeNumber = Integer(reader, "volume_number"), Summary = NullableText(reader, "summary")
        }, ct);
        await AddMissingAsync(rows, _db.Volumes, item => item.Id, "volumes", tracker, ct);
    }

    private async Task ImportContentDocumentsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, source_type, source_id, document_role, title,
                   mime_type, content_hash, version, status, created_at, updated_at
            FROM content_documents WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new ContentDocument
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = NullableText(reader, "project_id"),
            SourceType = Text(reader, "source_type"), SourceId = Text(reader, "source_id"),
            DocumentRole = Text(reader, "document_role"), Title = Text(reader, "title"), MimeType = Text(reader, "mime_type"),
            ContentHash = Text(reader, "content_hash"), Version = Integer(reader, "version"), Status = Text(reader, "status"),
            CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at")
        }, ct);
        await AddMissingAsync(rows, _db.ContentDocuments, item => item.Id, "content_documents", tracker, ct);
    }

    private async Task ImportContentChunksAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT chunk.id, chunk.document_id, chunk.chunk_index, chunk.chunk_text,
                   chunk.token_count, chunk.char_start, chunk.char_end, chunk.content_hash
            FROM content_chunks AS chunk
            JOIN content_documents AS document ON document.id = chunk.document_id
            WHERE document.user_id = $user_id ORDER BY chunk.document_id, chunk.chunk_index
            """, userId, reader => new ContentChunk
        {
            Id = Text(reader, "id"), DocumentId = Text(reader, "document_id"), ChunkIndex = Integer(reader, "chunk_index"),
            ChunkText = Text(reader, "chunk_text"), TokenCount = Integer(reader, "token_count"),
            CharStart = Integer(reader, "char_start"), CharEnd = Integer(reader, "char_end"), ContentHash = Text(reader, "content_hash")
        }, ct);
        await AddMissingAsync(rows, _db.ContentChunks, item => item.Id, "content_chunks", tracker, ct);
    }

    private async Task ImportChaptersAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT chapter.id, chapter.project_id, chapter.volume_id, chapter.title,
                   chapter.chapter_number, chapter.word_count, chapter.current_document_id,
                   chapter.status, chapter.created_at, chapter.updated_at, chapter.idempotency_key
            FROM chapters AS chapter
            JOIN novel_projects AS project ON project.id = chapter.project_id
            WHERE project.user_id = $user_id ORDER BY chapter.chapter_number, chapter.id
            """, userId, reader => new Chapter
        {
            Id = Text(reader, "id"), ProjectId = Text(reader, "project_id"), VolumeId = NullableText(reader, "volume_id"),
            Title = Text(reader, "title"), ChapterNumber = Integer(reader, "chapter_number"), WordCount = Integer(reader, "word_count"),
            CurrentDocumentId = NullableText(reader, "current_document_id"), Status = Text(reader, "status"),
            CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at"),
            IdempotencyKey = NullableText(reader, "idempotency_key")
        }, ct);
        await AddMissingAsync(rows, _db.Chapters, item => item.Id, "chapters", tracker, ct);
    }

    private async Task ImportChapterVersionsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, chapter_id, content_document_id, version_number,
                   title, word_count, status, runtime_run_id, package_id, gate_report_json,
                   agent_review_json, created_at
            FROM chapter_versions WHERE user_id = $user_id ORDER BY chapter_id, version_number
            """, userId, reader => new ChapterVersion
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            ChapterId = Text(reader, "chapter_id"), ContentDocumentId = Text(reader, "content_document_id"),
            VersionNumber = Integer(reader, "version_number"), Title = Text(reader, "title"), WordCount = Integer(reader, "word_count"),
            Status = Text(reader, "status"), RuntimeRunId = NullableText(reader, "runtime_run_id"), PackageId = NullableText(reader, "package_id"),
            GateReportJson = NullableText(reader, "gate_report_json"), AgentReviewJson = NullableText(reader, "agent_review_json"),
            CreatedAt = Timestamp(reader, "created_at")
        }, ct);
        await AddMissingAsync(rows, _db.ChapterVersions, item => item.Id, "chapter_versions", tracker, ct);
    }

    private async Task ImportStoryConstitutionsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, genre, sub_genre, core_hook, reader_promise,
                   genre_profile, target_audience, taboos, created_at, updated_at, idempotency_key
            FROM story_constitutions WHERE user_id = $user_id ORDER BY project_id
            """, userId, reader => new StoryConstitution
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            Genre = Text(reader, "genre"), SubGenre = NullableText(reader, "sub_genre"), CoreHook = Text(reader, "core_hook"),
            ReaderPromise = NullableText(reader, "reader_promise"), GenreProfile = NullableText(reader, "genre_profile"),
            TargetAudience = NullableText(reader, "target_audience"), Taboos = NullableText(reader, "taboos"),
            CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at"),
            IdempotencyKey = NullableText(reader, "idempotency_key")
        }, ct);
        await AddMissingAsync(rows, _db.StoryConstitutions, item => item.Id, "story_constitutions", tracker, ct);
    }

    private async Task ImportCharactersAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, name, role, alias, age, gender, appearance,
                   personality, background, initial_power_level, current_power_level,
                   special_abilities, core_goal, motivation, relationships, status,
                   first_appear_chapter, last_appear_chapter, created_at, updated_at, idempotency_key
            FROM characters WHERE user_id = $user_id ORDER BY project_id, name
            """, userId, reader => new Character
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            Name = Text(reader, "name"), Role = Text(reader, "role"), Alias = NullableText(reader, "alias"),
            Age = NullableInteger(reader, "age"), Gender = NullableText(reader, "gender"), Appearance = NullableText(reader, "appearance"),
            Personality = NullableText(reader, "personality"), Background = NullableText(reader, "background"),
            InitialPowerLevel = NullableText(reader, "initial_power_level"), CurrentPowerLevel = NullableText(reader, "current_power_level"),
            SpecialAbilities = NullableText(reader, "special_abilities"), CoreGoal = NullableText(reader, "core_goal"),
            Motivation = NullableText(reader, "motivation"), Relationships = NullableText(reader, "relationships"),
            Status = Text(reader, "status"), FirstAppearChapter = NullableText(reader, "first_appear_chapter"),
            LastAppearChapter = NullableText(reader, "last_appear_chapter"), CreatedAt = Timestamp(reader, "created_at"),
            UpdatedAt = Timestamp(reader, "updated_at"), IdempotencyKey = NullableText(reader, "idempotency_key")
        }, ct);
        await AddMissingAsync(rows, _db.Characters, item => item.Id, "characters", tracker, ct);
    }

    private async Task ImportStoryBibleExtensionsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var arcs = await ReadAsync(source, """
            SELECT id, user_id, project_id, volume_number, volume_title, volume_theme,
                   target_chapters, current_chapters, act1_setup, act2_confrontation,
                   act3_climax, act4_resolution, key_events, major_conflict,
                   conflict_escalation, status, created_at, updated_at, completed_at,
                   idempotency_key
            FROM volume_arcs WHERE user_id = $user_id ORDER BY project_id, volume_number
            """, userId, reader => new VolumeArc
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            VolumeNumber = Integer(reader, "volume_number"), VolumeTitle = Text(reader, "volume_title"),
            VolumeTheme = NullableText(reader, "volume_theme"), TargetChapters = NullableInteger(reader, "target_chapters"),
            CurrentChapters = Integer(reader, "current_chapters"), Act1Setup = NullableText(reader, "act1_setup"),
            Act2Confrontation = NullableText(reader, "act2_confrontation"), Act3Climax = NullableText(reader, "act3_climax"),
            Act4Resolution = NullableText(reader, "act4_resolution"), KeyEvents = NullableText(reader, "key_events"),
            MajorConflict = NullableText(reader, "major_conflict"), ConflictEscalation = NullableText(reader, "conflict_escalation"),
            Status = Text(reader, "status"), CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at"),
            CompletedAt = NullableTimestamp(reader, "completed_at"), IdempotencyKey = NullableText(reader, "idempotency_key")
        }, ct);
        await AddMissingAsync(arcs, _db.VolumeArcs, item => item.Id, "volume_arcs", tracker, ct);

        var world = await ReadAsync(source, """
            SELECT id, user_id, project_id, category, sub_category, title, content,
                   first_mentioned_chapter, referenced_chapters, version, previous_version,
                   change_log, created_at, updated_at
            FROM world_settings WHERE user_id = $user_id ORDER BY project_id, category, title
            """, userId, reader => new WorldSettingEntry
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            Category = Text(reader, "category"), SubCategory = NullableText(reader, "sub_category"),
            Title = Text(reader, "title"), Content = Text(reader, "content"),
            FirstMentionedChapter = NullableText(reader, "first_mentioned_chapter"),
            ReferencedChapters = NullableText(reader, "referenced_chapters"), Version = Integer(reader, "version"),
            PreviousVersion = NullableText(reader, "previous_version"), ChangeLog = NullableText(reader, "change_log"),
            CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at")
        }, ct);
        await AddMissingAsync(world, _db.WorldSettingEntries, item => item.Id, "world_settings", tracker, ct);

        var foreshadows = await ReadAsync(source, """
            SELECT id, user_id, project_id, title, content, category, planted_in_chapter,
                   planted_context, status, resolved_in_chapter, resolved_context,
                   planted_at, resolved_at, priority, created_at, updated_at
            FROM foreshadow_ledger WHERE user_id = $user_id ORDER BY project_id, planted_at, id
            """, userId, reader => new ForeshadowEntry
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            Title = Text(reader, "title"), Content = Text(reader, "content"), Category = Text(reader, "category"),
            PlantedInChapter = Text(reader, "planted_in_chapter"), PlantedContext = NullableText(reader, "planted_context"),
            Status = Text(reader, "status"), ResolvedInChapter = NullableText(reader, "resolved_in_chapter"),
            ResolvedContext = NullableText(reader, "resolved_context"), PlantedAt = Timestamp(reader, "planted_at"),
            ResolvedAt = NullableTimestamp(reader, "resolved_at"), Priority = Integer(reader, "priority"),
            CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at")
        }, ct);
        await AddMissingAsync(foreshadows, _db.ForeshadowEntries, item => item.Id, "foreshadow_ledger", tracker, ct);
    }

    private async Task ImportKnowledgeAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, entry_type, title, content, usage_count,
                   created_at, vector_id, source_type, source_upload_task_id, chunk_index,
                   extraction_context, tags, weight, is_archived, idempotency_key
            FROM knowledge_base WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new KnowledgeBase
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), SourceProjectId = NullableText(reader, "project_id"),
            EntryType = Text(reader, "entry_type"), Title = Text(reader, "title"), Content = Text(reader, "content"),
            UsageCount = Integer(reader, "usage_count"), CreatedAt = Timestamp(reader, "created_at"), VectorId = NullableText(reader, "vector_id"),
            SourceType = Text(reader, "source_type"), SourceUploadTaskId = NullableText(reader, "source_upload_task_id"),
            ChunkIndex = NullableInteger(reader, "chunk_index"), ExtractionContext = NullableText(reader, "extraction_context"),
            Tags = NullableText(reader, "tags"), Weight = Integer(reader, "weight"), IsArchived = Boolean(reader, "is_archived"),
            IdempotencyKey = NullableText(reader, "idempotency_key")
        }, ct);
        await AddMissingAsync(rows, _db.KnowledgeBases, item => item.Id, "knowledge_base", tracker, ct);

        var existingVersions = await _db.KnowledgeDocumentBlobs
            .Where(item => item.UserId == userId)
            .Select(item => item.KnowledgeVersion)
            .ToListAsync(ct);
        var nextVersion = existingVersions.Count == 0 ? 1 : existingVersions.Max() + 1;
        foreach (var row in rows)
        {
            var blobId = DerivedId("kb", row.Id);
            var entryId = DerivedId("ke", row.Id);
            if (await _db.KnowledgeEntries.AnyAsync(item => item.Id == entryId, ct))
            {
                tracker.Reused("knowledge_entries");
                continue;
            }
            var bytes = Encoding.UTF8.GetBytes(row.Content);
            var projectId = row.SourceProjectId ?? string.Empty;
            var blob = new KnowledgeDocumentBlob
            {
                Id = blobId, UserId = userId, ProjectId = projectId,
                FileName = $"legacy-{row.Id}.txt", MimeType = "text/plain; charset=utf-8", Data = bytes,
                ContentHash = Sha256(bytes), KnowledgeVersion = nextVersion++, Status = "processed",
                CreatedAt = row.CreatedAt, UpdatedAt = row.CreatedAt
            };
            _db.KnowledgeDocumentBlobs.Add(blob);
            _db.KnowledgeEntries.Add(new KnowledgeEntry
            {
                Id = entryId, UserId = userId, ProjectId = projectId, LogicalKnowledgeId = row.Id,
                DocumentBlobId = blob.Id, KnowledgeVersion = blob.KnowledgeVersion, Version = 1,
                SourceEntryIndex = 0, EntryType = row.EntryType, Title = row.Title, Content = row.Content,
                Summary = row.Content.Length <= 240 ? row.Content : row.Content[..240],
                Status = row.IsArchived ? "archived" : "active", CreatedAt = row.CreatedAt
            });
            tracker.Imported("knowledge_document_blobs");
            tracker.Imported("knowledge_entries");
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task ImportMemoriesAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, project_id, session_id, memory_type, memory_key, content, created_at, updated_at
            FROM agent_memories WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new LegacyMemory(
            Text(reader, "id"), NullableText(reader, "project_id"), NullableText(reader, "session_id"),
            Text(reader, "memory_type"), Text(reader, "memory_key"), Text(reader, "content"),
            Timestamp(reader, "created_at"), Timestamp(reader, "updated_at")), ct);
        foreach (var row in rows)
        {
            if (row.SessionId != null || row.MemoryType.StartsWith("session.", StringComparison.Ordinal))
            {
                tracker.Skip("agent_memories", row.Id, "transient_session_memory");
                continue;
            }
            if (row.ProjectId != null || row.MemoryType.StartsWith("project.", StringComparison.Ordinal))
            {
                tracker.Skip("agent_memories", row.Id, "unconfirmed_project_memory");
                continue;
            }
            if (!row.MemoryType.StartsWith("author.", StringComparison.Ordinal))
            {
                tracker.Skip("agent_memories", row.Id, "unsupported_legacy_memory");
                continue;
            }
            var id = DerivedId("am", row.Id);
            if (await _db.AuthorMemories.AnyAsync(item => item.Id == id, ct))
            {
                tracker.Reused("author_memories");
                continue;
            }
            _db.AuthorMemories.Add(new AuthorMemory
            {
                Id = id, UserId = userId, MemoryKind = row.MemoryType, ContentJson = EnsureJson(row.Content),
                Source = "sqlite_migration", Version = 1, Status = "active",
                CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt
            });
            tracker.Imported("author_memories");
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task ImportKnowledgeUsagesAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, knowledge_id, status, source_session_id,
                   source_run_id, first_seen_at, last_used_at, usage_count, note, role,
                   scope, priority, constraint_level, package_policy, bound_version,
                   used_by_chapters_json, usage_idempotency_keys_json
            FROM project_knowledge_usages WHERE user_id = $user_id ORDER BY first_seen_at, id
            """, userId, reader => new ProjectKnowledgeUsage
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            KnowledgeId = Text(reader, "knowledge_id"), Status = Text(reader, "status"),
            SourceSessionId = NullableText(reader, "source_session_id"), SourceRunId = NullableText(reader, "source_run_id"),
            FirstSeenAt = Timestamp(reader, "first_seen_at"), LastUsedAt = NullableTimestamp(reader, "last_used_at"),
            UsageCount = Integer(reader, "usage_count"), Note = NullableText(reader, "note"), Role = Text(reader, "role"),
            Scope = Text(reader, "scope"), Priority = Integer(reader, "priority"), ConstraintLevel = Text(reader, "constraint_level"),
            PackagePolicy = Text(reader, "package_policy"), BoundVersion = NullableText(reader, "bound_version"),
            UsedByChaptersJson = NullableText(reader, "used_by_chapters_json"),
            UsageIdempotencyKeysJson = NullableText(reader, "usage_idempotency_keys_json")
        }, ct);
        await AddMissingAsync(rows, _db.ProjectKnowledgeUsages, item => item.Id, "project_knowledge_usages", tracker, ct);
    }

    private async Task ImportAgentRunsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, run_type, target_chapter_id, status, input_params,
                   output_data, output_document_id, context_package_size, started_at, completed_at,
                   duration_ms, created_at, updated_at
            FROM agent_runs WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new AgentRun
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = Text(reader, "project_id"),
            RunType = Text(reader, "run_type"), TargetChapterId = NullableText(reader, "target_chapter_id"),
            Status = Text(reader, "status"), InputParams = NullableText(reader, "input_params"), OutputData = NullableText(reader, "output_data"),
            OutputDocumentId = NullableText(reader, "output_document_id"), ContextPackageSize = NullableInteger(reader, "context_package_size"),
            StartedAt = Timestamp(reader, "started_at"), CompletedAt = NullableTimestamp(reader, "completed_at"),
            DurationMs = NullableInteger(reader, "duration_ms"), CreatedAt = Timestamp(reader, "created_at"), UpdatedAt = Timestamp(reader, "updated_at")
        }, ct);
        foreach (var row in rows)
        {
            if (!TerminalRunStatuses.Contains(row.Status))
            {
                tracker.Skip("agent_runs", row.Id, "unfinished_react_run");
                continue;
            }
            await AddOneAsync(row, _db.AgentRuns, item => item.Id, "agent_runs", tracker, ct);
        }
    }

    private async Task ImportToolExecutionsAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var rows = await ReadAsync(source, """
            SELECT id, user_id, project_id, session_id, run_id, tool_name, phase, risk,
                   arguments_json, arguments_hash, side_effects_json, semantic_contract_json,
                   status, result_phase, result_message, error_type, error_message, failure_json,
                   artifact_json, recommended_next_tool, missing_prerequisite, started_at,
                   completed_at, duration_ms
            FROM agent_tool_executions WHERE user_id = $user_id ORDER BY started_at, id
            """, userId, reader => new AgentToolExecution
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), ProjectId = NullableText(reader, "project_id"),
            SessionId = Text(reader, "session_id"), RunId = NullableText(reader, "run_id"), ToolName = Text(reader, "tool_name"),
            Phase = Text(reader, "phase"), Risk = Text(reader, "risk"), ArgumentsJson = Text(reader, "arguments_json"),
            ArgumentsHash = Text(reader, "arguments_hash"), SideEffectsJson = Text(reader, "side_effects_json"),
            SemanticContractJson = Text(reader, "semantic_contract_json"), Status = Text(reader, "status"),
            ResultPhase = Text(reader, "result_phase"), ResultMessage = Text(reader, "result_message"),
            ErrorType = Text(reader, "error_type"), ErrorMessage = Text(reader, "error_message"), FailureJson = Text(reader, "failure_json"),
            ArtifactJson = Text(reader, "artifact_json"), RecommendedNextTool = Text(reader, "recommended_next_tool"),
            MissingPrerequisite = Text(reader, "missing_prerequisite"), StartedAt = Timestamp(reader, "started_at"),
            CompletedAt = NullableTimestamp(reader, "completed_at"), DurationMs = NullableInteger(reader, "duration_ms")
        }, ct);
        foreach (var row in rows)
        {
            if (!TerminalToolStatuses.Contains(row.Status))
            {
                tracker.Skip("agent_tool_executions", row.Id, "unfinished_tool_execution");
                continue;
            }
            await AddOneAsync(row, _db.AgentToolExecutions, item => item.Id, "agent_tool_executions", tracker, ct);
        }
    }

    private async Task ImportRuntimeAuditAsync(SqliteConnection source, string userId, ImportTracker tracker, CancellationToken ct)
    {
        var runs = await ReadAsync(source, """
            SELECT id, user_id, session_id, project_id, locked_project_id, executed_tools_json,
                   status, mode, current_phase, current_step, active_tool, user_message,
                   source_message_id, idempotency_key, budget_json, last_message, result_json,
                   error_message, failure_json, cancel_requested, started_at, completed_at,
                   created_at, updated_at
            FROM agent_runtime_runs WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new AgentRuntimeRun
        {
            Id = Text(reader, "id"), UserId = Text(reader, "user_id"), SessionId = Text(reader, "session_id"),
            ProjectId = NullableText(reader, "project_id"), LockedProjectId = NullableText(reader, "locked_project_id"),
            ExecutedToolsJson = Text(reader, "executed_tools_json"), Status = Text(reader, "status"), Mode = Text(reader, "mode"),
            CurrentPhase = Text(reader, "current_phase"), CurrentStep = Integer(reader, "current_step"),
            ActiveTool = Text(reader, "active_tool"), UserMessage = Text(reader, "user_message"),
            SourceMessageId = Text(reader, "source_message_id"), IdempotencyKey = Text(reader, "idempotency_key"),
            BudgetJson = Text(reader, "budget_json"), LastMessage = Text(reader, "last_message"), ResultJson = Text(reader, "result_json"),
            ErrorMessage = Text(reader, "error_message"), FailureJson = Text(reader, "failure_json"),
            CancelRequested = Boolean(reader, "cancel_requested"), StartedAt = NullableTimestamp(reader, "started_at"),
            CompletedAt = NullableTimestamp(reader, "completed_at"), CreatedAt = Timestamp(reader, "created_at"),
            UpdatedAt = Timestamp(reader, "updated_at")
        }, ct);
        var terminalRunIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in runs)
        {
            if (!TerminalRunStatuses.Contains(run.Status))
            {
                tracker.Skip("agent_runtime_runs", run.Id, "unfinished_react_run");
                continue;
            }
            terminalRunIds.Add(run.Id);
            await AddOneAsync(run, _db.AgentRuntimeRuns, item => item.Id, "agent_runtime_runs", tracker, ct);
        }

        var events = await ReadAsync(source, """
            SELECT id, runtime_run_id, user_id, session_id, project_id, type, stage, status,
                   artifact_type, artifact_id, display_surface, display_policy, message,
                   data_json, created_at
            FROM agent_runtime_events WHERE user_id = $user_id ORDER BY created_at, id
            """, userId, reader => new AgentRuntimeEvent
        {
            Id = Text(reader, "id"), RuntimeRunId = Text(reader, "runtime_run_id"), UserId = Text(reader, "user_id"),
            SessionId = Text(reader, "session_id"), ProjectId = NullableText(reader, "project_id"), Type = Text(reader, "type"),
            Stage = Text(reader, "stage"), Status = Text(reader, "status"), ArtifactType = Text(reader, "artifact_type"),
            ArtifactId = Text(reader, "artifact_id"), DisplaySurface = Text(reader, "display_surface"),
            DisplayPolicy = Text(reader, "display_policy"), Message = Text(reader, "message"),
            DataJson = Text(reader, "data_json"), CreatedAt = Timestamp(reader, "created_at")
        }, ct);
        foreach (var evt in events)
        {
            if (!terminalRunIds.Contains(evt.RuntimeRunId))
            {
                tracker.Skip("agent_runtime_events", evt.Id, "unfinished_react_event");
                continue;
            }
            await AddOneAsync(evt, _db.AgentRuntimeEvents, item => item.Id, "agent_runtime_events", tracker, ct);
        }
    }

    private async Task AddMissingAsync<TEntity>(
        IReadOnlyList<TEntity> rows,
        DbSet<TEntity> set,
        Func<TEntity, string> id,
        string entityType,
        ImportTracker tracker,
        CancellationToken ct) where TEntity : class
    {
        foreach (var row in rows)
            await AddOneAsync(row, set, id, entityType, tracker, ct);
    }

    private async Task AddOneAsync<TEntity>(
        TEntity row,
        DbSet<TEntity> set,
        Func<TEntity, string> id,
        string entityType,
        ImportTracker tracker,
        CancellationToken ct) where TEntity : class
    {
        var key = id(row);
        if (await set.FindAsync([key], ct) != null) tracker.Reused(entityType);
        else { set.Add(row); tracker.Imported(entityType); await _db.SaveChangesAsync(ct); }
    }

    private static async Task ValidateSchemaAsync(SqliteConnection source, CancellationToken ct)
    {
        foreach (var (table, expectedColumns) in RequiredSchema)
        {
            var actualColumns = await ReadAsync(
                source,
                $"PRAGMA table_info(\"{table}\")",
                null,
                reader => Text(reader, "name"),
                ct);
            if (actualColumns.Count == 0)
                throw new InvalidOperationException($"SQLite staging 缺少必需表：{table}。");
            var missing = expectedColumns.Except(actualColumns, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException($"SQLite staging 表 {table} 缺少列：{string.Join(", ", missing)}。");
        }
    }

    internal static async Task<IReadOnlyList<T>> ReadAsync<T>(
        SqliteConnection source,
        string sql,
        string? userId,
        Func<SqliteDataReader, T> map,
        CancellationToken ct)
    {
        await using var command = source.CreateCommand();
        command.CommandText = sql;
        if (userId != null)
            command.Parameters.AddWithValue("$user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
            rows.Add(map(reader));
        return rows;
    }

    internal static string Text(SqliteDataReader reader, string column) => reader.GetString(reader.GetOrdinal(column));
    internal static string? NullableText(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
    internal static int Integer(SqliteDataReader reader, string column) => Convert.ToInt32(reader.GetInt64(reader.GetOrdinal(column)));
    internal static int? NullableInteger(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetInt64(ordinal));
    }
    internal static bool Boolean(SqliteDataReader reader, string column) => reader.GetInt64(reader.GetOrdinal(column)) != 0;
    internal static float Real(SqliteDataReader reader, string column) => Convert.ToSingle(reader.GetDouble(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);
    internal static DateTime Timestamp(SqliteDataReader reader, string column) =>
        DateTime.Parse(Text(reader, column), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    internal static DateTime? NullableTimestamp(SqliteDataReader reader, string column)
    {
        var value = NullableText(reader, column);
        return value == null ? null : DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
    internal static string DerivedId(string prefix, string sourceId) =>
        sourceId.Length + prefix.Length + 1 <= 50 ? $"{prefix}-{sourceId}" : $"{prefix}-{Sha256(Encoding.UTF8.GetBytes(sourceId))[..32]}";
    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string EnsureJson(string value)
    {
        try { using var _ = JsonDocument.Parse(value); return value; }
        catch (JsonException) { return JsonSerializer.Serialize(value); }
    }

    private sealed record LegacyMemory(
        string Id,
        string? ProjectId,
        string? SessionId,
        string MemoryType,
        string MemoryKey,
        string Content,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed class ImportTracker(string userId)
    {
        private readonly Dictionary<string, int> _imported = new(StringComparer.Ordinal);
        private readonly List<MigrationSkippedItem> _skipped = [];
        private int _reused;

        public void Imported(string entity) => _imported[entity] = _imported.GetValueOrDefault(entity) + 1;
        public void Reused(string entity) => _reused++;
        public void Skip(string entity, string id, string reason) => _skipped.Add(new MigrationSkippedItem(entity, id, reason));
        public MigrationImportReport Report() => new(
            userId,
            _imported.Values.Sum(),
            _reused,
            _imported,
            _skipped);
    }
}
