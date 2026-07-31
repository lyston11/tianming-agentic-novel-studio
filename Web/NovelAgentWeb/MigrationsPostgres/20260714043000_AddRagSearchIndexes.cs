using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260714043000_AddRagSearchIndexes")]
public sealed class AddRagSearchIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE INDEX ix_rag_content_chunks_fts ON content_chunks
                USING gin (to_tsvector('simple', coalesce(chunk_text, '')));
            CREATE INDEX ix_rag_content_chunks_trgm ON content_chunks
                USING gin (chunk_text gin_trgm_ops);

            CREATE INDEX ix_rag_knowledge_chunks_fts ON knowledge_chunks
                USING gin (to_tsvector('simple', coalesce(text, '')));
            CREATE INDEX ix_rag_knowledge_chunks_trgm ON knowledge_chunks
                USING gin (text gin_trgm_ops);

            CREATE INDEX ix_rag_knowledge_sections_fts ON knowledge_sections
                USING gin (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, '')));
            CREATE INDEX ix_rag_knowledge_sections_trgm ON knowledge_sections
                USING gin ((coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(text, '')) gin_trgm_ops);

            CREATE INDEX ix_rag_knowledge_entries_fts ON knowledge_entries
                USING gin (to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, '')));
            CREATE INDEX ix_rag_knowledge_entries_trgm ON knowledge_entries
                USING gin ((coalesce(title, '') || ' ' || coalesce(summary, '') || ' ' || coalesce(content, '')) gin_trgm_ops);

            CREATE INDEX ix_rag_style_profiles_fts ON style_profiles
                USING gin (to_tsvector('simple', coalesce(features_json::text, '')));
            CREATE INDEX ix_rag_style_profiles_trgm ON style_profiles
                USING gin ((features_json::text) gin_trgm_ops);

            CREATE INDEX ix_rag_continuity_summaries_fts ON continuity_summaries
                USING gin (to_tsvector('simple', coalesce(summary_json::text, '')));
            CREATE INDEX ix_rag_continuity_summaries_trgm ON continuity_summaries
                USING gin ((summary_json::text) gin_trgm_ops);

            CREATE INDEX ix_rag_canon_changes_fts ON canon_changes
                USING gin (to_tsvector('simple', coalesce(subject, '') || ' ' || coalesce(change_json::text, '')));
            CREATE INDEX ix_rag_canon_changes_trgm ON canon_changes
                USING gin ((coalesce(subject, '') || ' ' || coalesce(change_json::text, '')) gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_rag_canon_changes_trgm;
            DROP INDEX IF EXISTS ix_rag_canon_changes_fts;
            DROP INDEX IF EXISTS ix_rag_continuity_summaries_trgm;
            DROP INDEX IF EXISTS ix_rag_continuity_summaries_fts;
            DROP INDEX IF EXISTS ix_rag_style_profiles_trgm;
            DROP INDEX IF EXISTS ix_rag_style_profiles_fts;
            DROP INDEX IF EXISTS ix_rag_knowledge_entries_trgm;
            DROP INDEX IF EXISTS ix_rag_knowledge_entries_fts;
            DROP INDEX IF EXISTS ix_rag_knowledge_sections_trgm;
            DROP INDEX IF EXISTS ix_rag_knowledge_sections_fts;
            DROP INDEX IF EXISTS ix_rag_knowledge_chunks_trgm;
            DROP INDEX IF EXISTS ix_rag_knowledge_chunks_fts;
            DROP INDEX IF EXISTS ix_rag_content_chunks_trgm;
            DROP INDEX IF EXISTS ix_rag_content_chunks_fts;
            """);
    }
}
