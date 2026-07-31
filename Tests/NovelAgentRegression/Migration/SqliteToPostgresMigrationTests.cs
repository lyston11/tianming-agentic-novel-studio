using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DataMigration;
using TM.Web.NovelAgentWeb.Services.Auth;
using Xunit;

namespace TM.Tests.NovelAgentRegression.Migration;

public sealed class SqliteToPostgresMigrationTests
{
    [Fact]
    public async Task ImportUserAsync_PreservesAuthoritativeContentAndSkipsTransientReactState()
    {
        await using var source = await CreateSourceAsync();
        await using var target = CreateTarget();
        var importer = new SqliteToPostgresImporter(target, new BackgroundUserContext());

        var first = await importer.ImportUserAsync(source, "user-1");
        var second = await importer.ImportUserAsync(source, "user-1");

        Assert.True(first.ImportedCount > 0);
        Assert.Equal(0, second.ImportedCount);
        Assert.True(second.ReusedCount > 0);
        Assert.Single(await target.Users.ToListAsync());
        Assert.Single(await target.UserSettings.ToListAsync());
        Assert.Null((await target.UserSettings.SingleAsync()).LlmApiKeyEncrypted);
        Assert.Single(await target.NovelProjects.ToListAsync());
        Assert.Single(await target.Chapters.ToListAsync());
        Assert.Single(await target.ChapterVersions.ToListAsync());
        Assert.Equal("chapter-body-hash", (await target.ContentDocuments.SingleAsync()).ContentHash);
        Assert.Single(await target.StoryConstitutions.ToListAsync());
        Assert.Single(await target.Characters.ToListAsync());
        Assert.Single(await target.VolumeArcs.ToListAsync());
        Assert.Single(await target.WorldSettingEntries.ToListAsync());
        Assert.Single(await target.ForeshadowEntries.ToListAsync());
        Assert.Single(await target.KnowledgeEntries.ToListAsync());
        Assert.Single(await target.KnowledgeDocumentBlobs.ToListAsync());
        Assert.Single(await target.ProjectKnowledgeUsages.ToListAsync());
        Assert.Single(await target.AuthorMemories.ToListAsync());
        Assert.Equal("author.style_likes", (await target.AuthorMemories.SingleAsync()).MemoryKind);
        Assert.Empty(await target.ProjectCollaborationDecisions.ToListAsync());
        Assert.Single(await target.AgentRuns.ToListAsync());
        Assert.Equal("Completed", (await target.AgentRuns.SingleAsync()).Status);
        Assert.Single(await target.AgentToolExecutions.ToListAsync());
        Assert.Equal("succeeded", (await target.AgentToolExecutions.SingleAsync()).Status);
        Assert.Single(await target.AgentRuntimeRuns.ToListAsync());
        Assert.Single(await target.AgentRuntimeEvents.ToListAsync());
        Assert.Contains(first.Skipped, item => item.Reason == "transient_session_memory");
        Assert.Contains(first.Skipped, item => item.Reason == "unconfirmed_project_memory");
        Assert.Contains(first.Skipped, item => item.Reason == "unfinished_react_run");
        Assert.Contains(first.Skipped, item => item.Reason == "unfinished_tool_execution");
    }

    [Fact]
    public async Task VerifyUserAsync_ComparesCountsAndContentHashes()
    {
        await using var source = await CreateSourceAsync();
        await using var target = CreateTarget();
        var importer = new SqliteToPostgresImporter(target, new BackgroundUserContext());
        await importer.ImportUserAsync(source, "user-1");
        var verifier = new MigrationVerifier(target, new BackgroundUserContext());

        var report = await verifier.VerifyUserAsync(source, "user-1");

        Assert.True(report.IsValid, string.Join(Environment.NewLine, report.Errors));
        Assert.All(report.Checks, check => Assert.True(check.Passed, check.Name));
        Assert.Contains(report.Checks, check => check.Name == "content_hashes" && check.Passed);
    }

    [Fact]
    public async Task VerifyUserAsync_RejectsSameCountWithCorruptedProjectFields()
    {
        await using var source = await CreateSourceAsync();
        await using var target = CreateTarget();
        var importer = new SqliteToPostgresImporter(target, new BackgroundUserContext());
        await importer.ImportUserAsync(source, "user-1");
        (await target.NovelProjects.SingleAsync()).Title = "被替换的标题";
        await target.SaveChangesAsync();
        var verifier = new MigrationVerifier(target, new BackgroundUserContext());

        var report = await verifier.VerifyUserAsync(source, "user-1");

        Assert.False(report.IsValid);
        Assert.Contains(report.Checks, check => check.Name == "projects" && !check.Passed);
    }

    [Fact]
    public async Task VerifyUserAsync_RejectsSameCountWithCorruptedContentChunk()
    {
        await using var source = await CreateSourceAsync();
        await using var target = CreateTarget();
        var importer = new SqliteToPostgresImporter(target, new BackgroundUserContext());
        await importer.ImportUserAsync(source, "user-1");
        (await target.ContentChunks.SingleAsync()).ChunkText = "被替换但数量不变的正文";
        await target.SaveChangesAsync();
        var verifier = new MigrationVerifier(target, new BackgroundUserContext());

        var report = await verifier.VerifyUserAsync(source, "user-1");

        Assert.False(report.IsValid);
        Assert.Contains(report.Checks, check => check.Name == "content_chunks" && !check.Passed);
    }

    private static NovelAgentDbContext CreateTarget()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task<SqliteConnection> CreateSourceAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE users (id TEXT PRIMARY KEY, username TEXT NOT NULL, email TEXT NOT NULL, password_hash TEXT NOT NULL, role TEXT NOT NULL, storage_quota_mb INTEGER NOT NULL, api_call_quota INTEGER NOT NULL, created_at TEXT NOT NULL, last_login_at TEXT NULL, is_active INTEGER NOT NULL);
            CREATE TABLE user_settings (user_id TEXT PRIMARY KEY, llm_provider TEXT NULL, llm_api_key_encrypted TEXT NULL, llm_base_url TEXT NULL, llm_model TEXT NULL, llm_temperature REAL NOT NULL, llm_max_tokens INTEGER NOT NULL, embedding_provider TEXT NOT NULL, embedding_model TEXT NOT NULL, agent_default_risk TEXT NOT NULL, agent_loop_auto_proceed INTEGER NOT NULL, agent_loop_max_steps INTEGER NOT NULL, default_genre TEXT NOT NULL, default_chapter_word_count INTEGER NOT NULL, theme TEXT NOT NULL, language TEXT NOT NULL);
            CREATE TABLE novel_projects (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, title TEXT NOT NULL, genre TEXT NULL, sub_genre TEXT NULL, core_hook TEXT NULL, status TEXT NOT NULL, word_count INTEGER NOT NULL, cover_image_url TEXT NULL, storage_project_name TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, idempotency_key TEXT NULL);
            CREATE TABLE volumes (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, title TEXT NOT NULL, volume_number INTEGER NOT NULL, summary TEXT NULL);
            CREATE TABLE chapters (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, volume_id TEXT NULL, title TEXT NOT NULL, chapter_number INTEGER NOT NULL, word_count INTEGER NOT NULL, current_document_id TEXT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, idempotency_key TEXT NULL);
            CREATE TABLE content_documents (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NULL, source_type TEXT NOT NULL, source_id TEXT NOT NULL, document_role TEXT NOT NULL, title TEXT NOT NULL, mime_type TEXT NOT NULL, content_hash TEXT NOT NULL, version INTEGER NOT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE content_chunks (id TEXT PRIMARY KEY, document_id TEXT NOT NULL, chunk_index INTEGER NOT NULL, chunk_text TEXT NOT NULL, token_count INTEGER NOT NULL, char_start INTEGER NOT NULL, char_end INTEGER NOT NULL, content_hash TEXT NOT NULL);
            CREATE TABLE chapter_versions (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, chapter_id TEXT NOT NULL, content_document_id TEXT NOT NULL, version_number INTEGER NOT NULL, title TEXT NOT NULL, word_count INTEGER NOT NULL, status TEXT NOT NULL, runtime_run_id TEXT NULL, package_id TEXT NULL, gate_report_json TEXT NULL, agent_review_json TEXT NULL, created_at TEXT NOT NULL);
            CREATE TABLE story_constitutions (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, genre TEXT NOT NULL, sub_genre TEXT NULL, core_hook TEXT NOT NULL, reader_promise TEXT NULL, genre_profile TEXT NULL, target_audience TEXT NULL, taboos TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, idempotency_key TEXT NULL);
            CREATE TABLE characters (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, name TEXT NOT NULL, role TEXT NOT NULL, alias TEXT NULL, age INTEGER NULL, gender TEXT NULL, appearance TEXT NULL, personality TEXT NULL, background TEXT NULL, initial_power_level TEXT NULL, current_power_level TEXT NULL, special_abilities TEXT NULL, core_goal TEXT NULL, motivation TEXT NULL, relationships TEXT NULL, status TEXT NOT NULL, first_appear_chapter TEXT NULL, last_appear_chapter TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, idempotency_key TEXT NULL);
            CREATE TABLE volume_arcs (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, volume_number INTEGER NOT NULL, volume_title TEXT NOT NULL, volume_theme TEXT NULL, target_chapters INTEGER NULL, current_chapters INTEGER NOT NULL, act1_setup TEXT NULL, act2_confrontation TEXT NULL, act3_climax TEXT NULL, act4_resolution TEXT NULL, key_events TEXT NULL, major_conflict TEXT NULL, conflict_escalation TEXT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, completed_at TEXT NULL, idempotency_key TEXT NULL);
            CREATE TABLE world_settings (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, category TEXT NOT NULL, sub_category TEXT NULL, title TEXT NOT NULL, content TEXT NOT NULL, first_mentioned_chapter TEXT NULL, referenced_chapters TEXT NULL, version INTEGER NOT NULL, previous_version TEXT NULL, change_log TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE foreshadow_ledger (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, title TEXT NOT NULL, content TEXT NOT NULL, category TEXT NOT NULL, planted_in_chapter TEXT NOT NULL, planted_context TEXT NULL, status TEXT NOT NULL, resolved_in_chapter TEXT NULL, resolved_context TEXT NULL, planted_at TEXT NOT NULL, resolved_at TEXT NULL, priority INTEGER NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE knowledge_base (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NULL, entry_type TEXT NOT NULL, title TEXT NOT NULL, content TEXT NOT NULL, usage_count INTEGER NOT NULL, created_at TEXT NOT NULL, vector_id TEXT NULL, source_type TEXT NOT NULL, source_upload_task_id TEXT NULL, chunk_index INTEGER NULL, extraction_context TEXT NULL, tags TEXT NULL, weight INTEGER NOT NULL, is_archived INTEGER NOT NULL, idempotency_key TEXT NULL);
            CREATE TABLE project_knowledge_usages (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, knowledge_id TEXT NOT NULL, status TEXT NOT NULL, source_session_id TEXT NULL, source_run_id TEXT NULL, first_seen_at TEXT NOT NULL, last_used_at TEXT NULL, usage_count INTEGER NOT NULL, note TEXT NULL, role TEXT NOT NULL, scope TEXT NOT NULL, priority INTEGER NOT NULL, constraint_level TEXT NOT NULL, package_policy TEXT NOT NULL, bound_version TEXT NULL, used_by_chapters_json TEXT NULL, usage_idempotency_keys_json TEXT NULL);
            CREATE TABLE agent_memories (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NULL, session_id TEXT NULL, memory_type TEXT NOT NULL, memory_key TEXT NOT NULL, content TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE agent_runs (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NOT NULL, run_type TEXT NOT NULL, target_chapter_id TEXT NULL, status TEXT NOT NULL, input_params TEXT NULL, output_data TEXT NULL, output_document_id TEXT NULL, context_package_size INTEGER NULL, started_at TEXT NOT NULL, completed_at TEXT NULL, duration_ms INTEGER NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE agent_tool_executions (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, project_id TEXT NULL, session_id TEXT NOT NULL, run_id TEXT NULL, tool_name TEXT NOT NULL, phase TEXT NOT NULL, risk TEXT NOT NULL, arguments_json TEXT NOT NULL, arguments_hash TEXT NOT NULL, side_effects_json TEXT NOT NULL, semantic_contract_json TEXT NOT NULL, status TEXT NOT NULL, result_phase TEXT NOT NULL, result_message TEXT NOT NULL, error_type TEXT NOT NULL, error_message TEXT NOT NULL, failure_json TEXT NOT NULL, artifact_json TEXT NOT NULL, recommended_next_tool TEXT NOT NULL, missing_prerequisite TEXT NOT NULL, started_at TEXT NOT NULL, completed_at TEXT NULL, duration_ms INTEGER NULL);
            CREATE TABLE agent_runtime_runs (id TEXT PRIMARY KEY, user_id TEXT NOT NULL, session_id TEXT NOT NULL, project_id TEXT NULL, locked_project_id TEXT NULL, executed_tools_json TEXT NOT NULL, status TEXT NOT NULL, mode TEXT NOT NULL, current_phase TEXT NOT NULL, current_step INTEGER NOT NULL, active_tool TEXT NOT NULL, user_message TEXT NOT NULL, source_message_id TEXT NOT NULL, idempotency_key TEXT NOT NULL, budget_json TEXT NOT NULL, last_message TEXT NOT NULL, result_json TEXT NOT NULL, error_message TEXT NOT NULL, failure_json TEXT NOT NULL, cancel_requested INTEGER NOT NULL, started_at TEXT NULL, completed_at TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE agent_runtime_events (id TEXT PRIMARY KEY, runtime_run_id TEXT NOT NULL, user_id TEXT NOT NULL, session_id TEXT NOT NULL, project_id TEXT NULL, type TEXT NOT NULL, stage TEXT NOT NULL, status TEXT NOT NULL, artifact_type TEXT NOT NULL, artifact_id TEXT NOT NULL, display_surface TEXT NOT NULL, display_policy TEXT NOT NULL, message TEXT NOT NULL, data_json TEXT NOT NULL, created_at TEXT NOT NULL);
            """);
        var now = "2026-07-18T00:00:00Z";
        var seedSql = """
            INSERT INTO users VALUES ('user-1','author','author@test.com','hash','author',5120,10000,'$NOW$',NULL,1);
            INSERT INTO user_settings VALUES ('user-1','openai','legacy-secret','http://model.local','model-1',0.7,4096,'local','embedding-1','Medium',1,12,'悬疑',3000,'dark','zh-CN');
            INSERT INTO novel_projects VALUES ('project-1','user-1','测试小说','悬疑',NULL,'核心钩子','active',1200,NULL,NULL,'$NOW$','$NOW$','project-key');
            INSERT INTO chapters VALUES ('chapter-1','project-1',NULL,'第一章',1,1200,'document-1','committed','$NOW$','$NOW$','chapter-key');
            INSERT INTO content_documents VALUES ('document-1','user-1','project-1','chapter','chapter-1','chapter_body','第一章','text/plain','chapter-body-hash',1,'active','$NOW$','$NOW$');
            INSERT INTO content_chunks VALUES ('chunk-1','document-1',0,'数据库正文',5,0,5,'chunk-hash');
            INSERT INTO chapter_versions VALUES ('version-1','user-1','project-1','chapter-1','document-1',1,'第一章',1200,'committed','run-completed',NULL,NULL,NULL,'$NOW$');
            INSERT INTO story_constitutions VALUES ('constitution-1','user-1','project-1','悬疑',NULL,'核心钩子','读者承诺',NULL,NULL,NULL,'$NOW$','$NOW$','constitution-key');
            INSERT INTO characters VALUES ('character-1','user-1','project-1','沈砚','protagonist',NULL,20,NULL,NULL,NULL,NULL,NULL,NULL,NULL,'活下去','寻找真相',NULL,'active',NULL,NULL,'$NOW$','$NOW$','character-key');
            INSERT INTO volume_arcs VALUES ('arc-1','user-1','project-1',1,'第一卷','旧邮路',10,1,NULL,NULL,NULL,NULL,'[]','生存','升级','in_progress','$NOW$','$NOW$',NULL,'arc-key');
            INSERT INTO world_settings VALUES ('world-1','user-1','project-1','location',NULL,'废城邮局','旧邮路入口','chapter-1','[\"chapter-1\"]',1,NULL,NULL,'$NOW$','$NOW$');
            INSERT INTO foreshadow_ledger VALUES ('foreshadow-1','user-1','project-1','第二次敲击','分拣台再次敲响','plot','chapter-1',NULL,'planted',NULL,NULL,'$NOW$',NULL,8,'$NOW$','$NOW$');
            INSERT INTO knowledge_base VALUES ('knowledge-1','user-1','project-1','HardFact','旧邮路','旧邮路不能逆行',2,'$NOW$',NULL,'manual',NULL,NULL,NULL,'[]',5,0,'knowledge-key');
            INSERT INTO project_knowledge_usages VALUES ('usage-1','user-1','project-1','knowledge-1','used','session-1','run-completed','$NOW$','$NOW$',2,NULL,'Reference','ProjectWide',50,'Reference','RelevantOnly','1','[\"chapter-1\"]','[]');
            INSERT INTO agent_memories VALUES ('memory-author','user-1',NULL,NULL,'author.style_likes','style_likes','[\"克制\"]','$NOW$','$NOW$');
            INSERT INTO agent_memories VALUES ('memory-session','user-1','project-1','session-1','session.current_goal','current_goal','写第一章','$NOW$','$NOW$');
            INSERT INTO agent_memories VALUES ('memory-project','user-1','project-1',NULL,'project.long_term_goal','long_term_goal','完成全书','$NOW$','$NOW$');
            INSERT INTO agent_runs VALUES ('run-completed','user-1','project-1','chapter_generation','chapter-1','Completed','{}','{}','document-1',10,'$NOW$','$NOW$',10,'$NOW$','$NOW$');
            INSERT INTO agent_runs VALUES ('run-planning','user-1','project-1','chapter_generation','chapter-1','Planning','{}',NULL,NULL,NULL,'$NOW$',NULL,NULL,'$NOW$','$NOW$');
            INSERT INTO agent_tool_executions VALUES ('tool-completed','user-1','project-1','session-1','run-completed','ProduceChapter','production','High','{}','hash','{}','{}','succeeded','completed','完成','','','{}','{}','','','$NOW$','$NOW$',10);
            INSERT INTO agent_tool_executions VALUES ('tool-running','user-1','project-1','session-1','run-planning','ProduceChapter','production','High','{}','hash-2','{}','{}','running','running','','','','{}','{}','','','$NOW$',NULL,NULL);
            INSERT INTO agent_runtime_runs VALUES ('runtime-completed','user-1','session-1','project-1','project-1','[]','completed','execute','completed',2,'','写第一章','message-1','runtime-key-1','{}','完成','{}','', '{}',0,'$NOW$','$NOW$','$NOW$','$NOW$');
            INSERT INTO agent_runtime_runs VALUES ('runtime-running','user-1','session-1','project-1','project-1','[]','running','execute','writing',1,'ProduceChapter','写第一章','message-2','runtime-key-2','{}','运行','{}','', '{}',0,'$NOW$',NULL,'$NOW$','$NOW$');
            INSERT INTO agent_runtime_events VALUES ('event-completed','runtime-completed','user-1','session-1','project-1','run_update','completed','completed','','','workflow','timeline','完成','{}','$NOW$');
            INSERT INTO agent_runtime_events VALUES ('event-running','runtime-running','user-1','session-1','project-1','run_update','writing','running','','','workflow','timeline','运行','{}','$NOW$');
            """;
        await ExecuteAsync(connection, seedSql.Replace("$NOW$", now, StringComparison.Ordinal));
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
