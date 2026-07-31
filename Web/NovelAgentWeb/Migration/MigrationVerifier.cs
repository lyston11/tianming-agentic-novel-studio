using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;
using System.Text.Json;

namespace TM.Web.NovelAgentWeb.DataMigration;

public sealed class MigrationVerifier
{
    private readonly NovelAgentDbContext _db;
    private readonly IBackgroundUserContext _backgroundUsers;

    public MigrationVerifier(NovelAgentDbContext db, IBackgroundUserContext backgroundUsers)
    {
        _db = db;
        _backgroundUsers = backgroundUsers;
    }

    public async Task<MigrationVerificationReport> VerifyUserAsync(
        SqliteConnection source,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var userScope = _backgroundUsers.Push(userId);
        var checks = new List<MigrationVerificationCheck>();
        checks.Add(await VerifyRowsAsync(source, userId, "users",
            "SELECT id, username, email, password_hash, role, storage_quota_mb, api_call_quota, created_at, last_login_at, is_active FROM users WHERE id = $user_id",
            r => Row(T(r, "id"), T(r, "username"), T(r, "email"), T(r, "password_hash"), T(r, "role"), I(r, "storage_quota_mb"), I(r, "api_call_quota"), D(r, "created_at"), ND(r, "last_login_at"), B(r, "is_active")),
            (await _db.Users.AsNoTracking().Where(x => x.Id == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.Username, x.Email, x.PasswordHash, x.Role, x.StorageQuotaMb, x.ApiCallQuota, x.CreatedAt, x.LastLoginAt, x.IsActive)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "projects",
            "SELECT id, user_id, title, genre, sub_genre, core_hook, status, word_count, cover_image_url, created_at, updated_at, idempotency_key FROM novel_projects WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "title"), N(r, "genre"), N(r, "sub_genre"), N(r, "core_hook"), T(r, "status"), I(r, "word_count"), N(r, "cover_image_url"), D(r, "created_at"), D(r, "updated_at"), N(r, "idempotency_key")),
            (await _db.NovelProjects.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.Title, x.Genre, x.SubGenre, x.CoreHook, x.Status, x.WordCount, x.CoverImageUrl, x.CreatedAt, x.UpdatedAt, x.IdempotencyKey)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "volumes",
            "SELECT volume.id, volume.project_id, volume.title, volume.volume_number, volume.summary FROM volumes volume JOIN novel_projects project ON project.id = volume.project_id WHERE project.user_id = $user_id",
            r => Row(T(r, "id"), T(r, "project_id"), T(r, "title"), I(r, "volume_number"), N(r, "summary")),
            (await _db.Volumes.AsNoTracking().Where(x => _db.NovelProjects.Where(p => p.UserId == userId).Select(p => p.Id).Contains(x.ProjectId)).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.ProjectId, x.Title, x.VolumeNumber, x.Summary)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "chapters",
            "SELECT chapter.id, chapter.project_id, chapter.volume_id, chapter.title, chapter.chapter_number, chapter.word_count, chapter.current_document_id, chapter.status, chapter.created_at, chapter.updated_at, chapter.idempotency_key FROM chapters chapter JOIN novel_projects project ON project.id = chapter.project_id WHERE project.user_id = $user_id",
            r => Row(T(r, "id"), T(r, "project_id"), N(r, "volume_id"), T(r, "title"), I(r, "chapter_number"), I(r, "word_count"), N(r, "current_document_id"), T(r, "status"), D(r, "created_at"), D(r, "updated_at"), N(r, "idempotency_key")),
            (await _db.Chapters.AsNoTracking().Where(x => _db.NovelProjects.Where(p => p.UserId == userId).Select(p => p.Id).Contains(x.ProjectId)).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.ProjectId, x.VolumeId, x.Title, x.ChapterNumber, x.WordCount, x.CurrentDocumentId, x.Status, x.CreatedAt, x.UpdatedAt, x.IdempotencyKey)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "chapter_versions",
            "SELECT id, user_id, project_id, chapter_id, content_document_id, version_number, title, word_count, status, runtime_run_id, package_id, gate_report_json, agent_review_json, created_at FROM chapter_versions WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), T(r, "chapter_id"), T(r, "content_document_id"), I(r, "version_number"), T(r, "title"), I(r, "word_count"), T(r, "status"), N(r, "runtime_run_id"), N(r, "package_id"), N(r, "gate_report_json"), N(r, "agent_review_json"), D(r, "created_at")),
            (await _db.ChapterVersions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.ChapterId, x.ContentDocumentId, x.VersionNumber, x.Title, x.WordCount, x.Status, x.RuntimeRunId, x.PackageId, x.GateReportJson, x.AgentReviewJson, x.CreatedAt)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "content_hashes",
            "SELECT id, user_id, project_id, source_type, source_id, document_role, title, mime_type, content_hash, version, status, created_at, updated_at FROM content_documents WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), N(r, "project_id"), T(r, "source_type"), T(r, "source_id"), T(r, "document_role"), T(r, "title"), T(r, "mime_type"), T(r, "content_hash"), I(r, "version"), T(r, "status"), D(r, "created_at"), D(r, "updated_at")),
            (await _db.ContentDocuments.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.SourceType, x.SourceId, x.DocumentRole, x.Title, x.MimeType, x.ContentHash, x.Version, x.Status, x.CreatedAt, x.UpdatedAt)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "content_chunks",
            "SELECT chunk.id, chunk.document_id, chunk.chunk_index, chunk.chunk_text, chunk.token_count, chunk.char_start, chunk.char_end, chunk.content_hash FROM content_chunks chunk JOIN content_documents document ON document.id = chunk.document_id WHERE document.user_id = $user_id",
            r => Row(T(r, "id"), T(r, "document_id"), I(r, "chunk_index"), T(r, "chunk_text"), I(r, "token_count"), I(r, "char_start"), I(r, "char_end"), T(r, "content_hash")),
            (await _db.ContentChunks.AsNoTracking().Where(x => _db.ContentDocuments.Where(d => d.UserId == userId).Select(d => d.Id).Contains(x.DocumentId)).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.DocumentId, x.ChunkIndex, x.ChunkText, x.TokenCount, x.CharStart, x.CharEnd, x.ContentHash)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "story_constitutions",
            "SELECT id, user_id, project_id, genre, sub_genre, core_hook, reader_promise, genre_profile, target_audience, taboos, created_at, updated_at, idempotency_key FROM story_constitutions WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), T(r, "genre"), N(r, "sub_genre"), T(r, "core_hook"), N(r, "reader_promise"), N(r, "genre_profile"), N(r, "target_audience"), N(r, "taboos"), D(r, "created_at"), D(r, "updated_at"), N(r, "idempotency_key")),
            (await _db.StoryConstitutions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.Genre, x.SubGenre, x.CoreHook, x.ReaderPromise, x.GenreProfile, x.TargetAudience, x.Taboos, x.CreatedAt, x.UpdatedAt, x.IdempotencyKey)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "characters",
            "SELECT id, user_id, project_id, name, role, alias, age, gender, appearance, personality, background, initial_power_level, current_power_level, special_abilities, core_goal, motivation, relationships, status, first_appear_chapter, last_appear_chapter, created_at, updated_at, idempotency_key FROM characters WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), T(r, "name"), T(r, "role"), N(r, "alias"), NI(r, "age"), N(r, "gender"), N(r, "appearance"), N(r, "personality"), N(r, "background"), N(r, "initial_power_level"), N(r, "current_power_level"), N(r, "special_abilities"), N(r, "core_goal"), N(r, "motivation"), N(r, "relationships"), T(r, "status"), N(r, "first_appear_chapter"), N(r, "last_appear_chapter"), D(r, "created_at"), D(r, "updated_at"), N(r, "idempotency_key")),
            (await _db.Characters.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.Name, x.Role, x.Alias, x.Age, x.Gender, x.Appearance, x.Personality, x.Background, x.InitialPowerLevel, x.CurrentPowerLevel, x.SpecialAbilities, x.CoreGoal, x.Motivation, x.Relationships, x.Status, x.FirstAppearChapter, x.LastAppearChapter, x.CreatedAt, x.UpdatedAt, x.IdempotencyKey)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "volume_arcs",
            "SELECT id, user_id, project_id, volume_number, volume_title, volume_theme, target_chapters, current_chapters, act1_setup, act2_confrontation, act3_climax, act4_resolution, key_events, major_conflict, conflict_escalation, status, created_at, updated_at, completed_at, idempotency_key FROM volume_arcs WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), I(r, "volume_number"), T(r, "volume_title"), N(r, "volume_theme"), NI(r, "target_chapters"), I(r, "current_chapters"), N(r, "act1_setup"), N(r, "act2_confrontation"), N(r, "act3_climax"), N(r, "act4_resolution"), N(r, "key_events"), N(r, "major_conflict"), N(r, "conflict_escalation"), T(r, "status"), D(r, "created_at"), D(r, "updated_at"), ND(r, "completed_at"), N(r, "idempotency_key")),
            (await _db.VolumeArcs.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.VolumeNumber, x.VolumeTitle, x.VolumeTheme, x.TargetChapters, x.CurrentChapters, x.Act1Setup, x.Act2Confrontation, x.Act3Climax, x.Act4Resolution, x.KeyEvents, x.MajorConflict, x.ConflictEscalation, x.Status, x.CreatedAt, x.UpdatedAt, x.CompletedAt, x.IdempotencyKey)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "world_settings",
            "SELECT id, user_id, project_id, category, sub_category, title, content, first_mentioned_chapter, referenced_chapters, version, previous_version, change_log, created_at, updated_at FROM world_settings WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), T(r, "category"), N(r, "sub_category"), T(r, "title"), T(r, "content"), N(r, "first_mentioned_chapter"), N(r, "referenced_chapters"), I(r, "version"), N(r, "previous_version"), N(r, "change_log"), D(r, "created_at"), D(r, "updated_at")),
            (await _db.WorldSettingEntries.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.Category, x.SubCategory, x.Title, x.Content, x.FirstMentionedChapter, x.ReferencedChapters, x.Version, x.PreviousVersion, x.ChangeLog, x.CreatedAt, x.UpdatedAt)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "foreshadow_ledger",
            "SELECT id, user_id, project_id, title, content, category, planted_in_chapter, planted_context, status, resolved_in_chapter, resolved_context, planted_at, resolved_at, priority, created_at, updated_at FROM foreshadow_ledger WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), T(r, "title"), T(r, "content"), T(r, "category"), T(r, "planted_in_chapter"), N(r, "planted_context"), T(r, "status"), N(r, "resolved_in_chapter"), N(r, "resolved_context"), D(r, "planted_at"), ND(r, "resolved_at"), I(r, "priority"), D(r, "created_at"), D(r, "updated_at")),
            (await _db.ForeshadowEntries.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.Title, x.Content, x.Category, x.PlantedInChapter, x.PlantedContext, x.Status, x.ResolvedInChapter, x.ResolvedContext, x.PlantedAt, x.ResolvedAt, x.Priority, x.CreatedAt, x.UpdatedAt)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "knowledge_entries",
            "SELECT id, user_id, project_id, entry_type, title, content, usage_count, created_at, vector_id, source_type, source_upload_task_id, chunk_index, extraction_context, tags, weight, is_archived, idempotency_key FROM knowledge_base WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), N(r, "project_id"), T(r, "entry_type"), T(r, "title"), T(r, "content"), I(r, "usage_count"), D(r, "created_at"), N(r, "vector_id"), T(r, "source_type"), N(r, "source_upload_task_id"), NI(r, "chunk_index"), N(r, "extraction_context"), N(r, "tags"), I(r, "weight"), B(r, "is_archived"), N(r, "idempotency_key")),
            (await _db.KnowledgeBases.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.SourceProjectId, x.EntryType, x.Title, x.Content, x.UsageCount, x.CreatedAt, x.VectorId, x.SourceType, x.SourceUploadTaskId, x.ChunkIndex, x.ExtractionContext, x.Tags, x.Weight, x.IsArchived, x.IdempotencyKey)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "project_knowledge_usages",
            "SELECT id, user_id, project_id, knowledge_id, status, source_session_id, source_run_id, first_seen_at, last_used_at, usage_count, note, role, scope, priority, constraint_level, package_policy, bound_version, used_by_chapters_json, usage_idempotency_keys_json FROM project_knowledge_usages WHERE user_id = $user_id",
            r => Row(T(r, "id"), T(r, "user_id"), T(r, "project_id"), T(r, "knowledge_id"), T(r, "status"), N(r, "source_session_id"), N(r, "source_run_id"), D(r, "first_seen_at"), ND(r, "last_used_at"), I(r, "usage_count"), N(r, "note"), T(r, "role"), T(r, "scope"), I(r, "priority"), T(r, "constraint_level"), T(r, "package_policy"), N(r, "bound_version"), N(r, "used_by_chapters_json"), N(r, "usage_idempotency_keys_json")),
            (await _db.ProjectKnowledgeUsages.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.UserId, x.ProjectId, x.KnowledgeId, x.Status, x.SourceSessionId, x.SourceRunId, x.FirstSeenAt, x.LastUsedAt, x.UsageCount, x.Note, x.Role, x.Scope, x.Priority, x.ConstraintLevel, x.PackagePolicy, x.BoundVersion, x.UsedByChaptersJson, x.UsageIdempotencyKeysJson)), cancellationToken));
        checks.Add(await VerifyRowsAsync(source, userId, "author_memories",
            "SELECT id, memory_type, content FROM agent_memories WHERE user_id = $user_id AND project_id IS NULL AND session_id IS NULL AND memory_type LIKE 'author.%'",
            r => Row(SqliteToPostgresImporter.DerivedId("am", T(r, "id")), T(r, "memory_type"), NormalizeJson(N(r, "content")!)),
            (await _db.AuthorMemories.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(cancellationToken))
                .Select(x => Row(x.Id, x.MemoryKind, x.ContentJson)), cancellationToken));

        var errors = checks.Where(check => !check.Passed)
            .Select(check => $"{check.Name}: expected={check.Expected}, actual={check.Actual}. {check.Detail}")
            .ToArray();
        return new MigrationVerificationReport(userId, errors.Length == 0, checks, errors);
    }

    public async Task<MigrationVerificationReport> VerifyUserAsync(
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
        return await VerifyUserAsync(source, userId, cancellationToken);
    }

    private static async Task<MigrationVerificationCheck> VerifyRowsAsync(
        SqliteConnection source,
        string userId,
        string name,
        string sql,
        Func<SqliteDataReader, string> map,
        IEnumerable<string> targetRows,
        CancellationToken ct)
    {
        var expected = (await SqliteToPostgresImporter.ReadAsync(source, sql, userId, map, ct))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = targetRows.Order(StringComparer.Ordinal).ToArray();
        return new MigrationVerificationCheck(
            name,
            expected.LongLength,
            actual.LongLength,
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            "稳定 ID、所有权关系和关键字段必须逐项一致。");
    }

    private static string Row(params object?[] values) => JsonSerializer.Serialize(values);
    private static string T(SqliteDataReader reader, string column) => SqliteToPostgresImporter.Text(reader, column);
    private static string? N(SqliteDataReader reader, string column) => SqliteToPostgresImporter.NullableText(reader, column);
    private static int I(SqliteDataReader reader, string column) => SqliteToPostgresImporter.Integer(reader, column);
    private static int? NI(SqliteDataReader reader, string column) => SqliteToPostgresImporter.NullableInteger(reader, column);
    private static bool B(SqliteDataReader reader, string column) => SqliteToPostgresImporter.Boolean(reader, column);
    private static DateTime D(SqliteDataReader reader, string column) => SqliteToPostgresImporter.Timestamp(reader, column);
    private static DateTime? ND(SqliteDataReader reader, string column) => SqliteToPostgresImporter.NullableTimestamp(reader, column);
    private static string NormalizeJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(value);
        }
    }
}
