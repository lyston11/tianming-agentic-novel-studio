using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations;

[DbContext(typeof(NovelAgentDbContext))]
[Migration("20260712100000_FormalizeLegacyRuntimeSchema")]
public sealed class FormalizeLegacyRuntimeSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS chapter_drafts (
                id TEXT NOT NULL PRIMARY KEY,
                user_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                runtime_run_id TEXT NOT NULL,
                chapter_id TEXT NOT NULL,
                package_id TEXT NULL,
                artifact_id TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'draft_generated',
                draft_content TEXT NOT NULL,
                changes_json TEXT NULL,
                content_length INTEGER NOT NULL,
                repair_attempt_count INTEGER NOT NULL,
                has_changes INTEGER NOT NULL,
                generated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT FK_chapter_drafts_novel_projects_project_id
                    FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_chapter_drafts_project_chapter_created
                ON chapter_drafts (project_id, chapter_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_chapter_drafts_project_status
                ON chapter_drafts (project_id, status);
            CREATE INDEX IF NOT EXISTS idx_chapter_drafts_run_created
                ON chapter_drafts (runtime_run_id, created_at);

            CREATE TABLE IF NOT EXISTS generation_gate_reports (
                id TEXT NOT NULL PRIMARY KEY,
                user_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                runtime_run_id TEXT NOT NULL,
                chapter_id TEXT NOT NULL,
                package_id TEXT NULL,
                artifact_id TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                report_json TEXT NOT NULL,
                protocol_passed INTEGER NOT NULL,
                changes_detected INTEGER NOT NULL,
                fact_snapshot_passed INTEGER NOT NULL,
                blueprint_passed INTEGER NOT NULL,
                rag_passed INTEGER NOT NULL,
                issue_count INTEGER NOT NULL,
                repair_hint_count INTEGER NOT NULL,
                validated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT FK_generation_gate_reports_novel_projects_project_id
                    FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_generation_gate_reports_project_chapter_created
                ON generation_gate_reports (project_id, chapter_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_generation_gate_reports_project_status
                ON generation_gate_reports (project_id, status);
            CREATE INDEX IF NOT EXISTS idx_generation_gate_reports_run_created
                ON generation_gate_reports (runtime_run_id, created_at);

            CREATE TABLE IF NOT EXISTS agent_reviews (
                id TEXT NOT NULL PRIMARY KEY,
                user_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                runtime_run_id TEXT NOT NULL,
                chapter_id TEXT NOT NULL,
                package_id TEXT NULL,
                review_id TEXT NOT NULL,
                overall_result TEXT NOT NULL DEFAULT 'Unknown',
                validation_overall_result TEXT NOT NULL,
                requires_rewrite INTEGER NOT NULL,
                quality_score INTEGER NOT NULL,
                content_length INTEGER NOT NULL,
                check_count INTEGER NOT NULL,
                summary TEXT NOT NULL,
                meets_accepted_creative_intents INTEGER NOT NULL DEFAULT 1,
                continuity_risk TEXT NOT NULL DEFAULT '',
                chapter_pacing TEXT NOT NULL DEFAULT '',
                recommended_action TEXT NOT NULL DEFAULT '',
                review_json TEXT NOT NULL,
                reviewed_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT FK_agent_reviews_novel_projects_project_id
                    FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_agent_reviews_project_chapter_created
                ON agent_reviews (project_id, chapter_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_agent_reviews_project_result
                ON agent_reviews (project_id, overall_result);
            CREATE INDEX IF NOT EXISTS idx_agent_reviews_run_created
                ON agent_reviews (runtime_run_id, created_at);

            CREATE TABLE IF NOT EXISTS agent_memory_reads (
                id TEXT NOT NULL PRIMARY KEY,
                user_id TEXT NOT NULL,
                project_id TEXT NULL,
                session_id TEXT NULL,
                run_id TEXT NULL,
                memory_scope TEXT NOT NULL,
                memory_keys_json TEXT NOT NULL DEFAULT '[]',
                source_type TEXT NOT NULL DEFAULT 'memory_repository',
                consumer TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT FK_agent_memory_reads_users_user_id
                    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
                CONSTRAINT FK_agent_memory_reads_novel_projects_project_id
                    FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_agent_memory_reads_user_created
                ON agent_memory_reads (user_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_reads_project_created
                ON agent_memory_reads (project_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_reads_session
                ON agent_memory_reads (session_id);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_reads_run
                ON agent_memory_reads (run_id);

            CREATE TABLE IF NOT EXISTS agent_memory_promotions (
                id TEXT NOT NULL PRIMARY KEY,
                user_id TEXT NOT NULL,
                project_id TEXT NULL,
                session_id TEXT NULL,
                run_id TEXT NULL,
                source_scope TEXT NOT NULL,
                target_scope TEXT NOT NULL,
                source_memory_key TEXT NOT NULL,
                target_memory_key TEXT NOT NULL,
                promotion_reason TEXT NOT NULL,
                payload_json TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT FK_agent_memory_promotions_users_user_id
                    FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
                CONSTRAINT FK_agent_memory_promotions_novel_projects_project_id
                    FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_agent_memory_promotions_user_created
                ON agent_memory_promotions (user_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_promotions_project_created
                ON agent_memory_promotions (project_id, created_at);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_promotions_session
                ON agent_memory_promotions (session_id);
            CREATE INDEX IF NOT EXISTS idx_agent_memory_promotions_run
                ON agent_memory_promotions (run_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS agent_memory_promotions;
            DROP TABLE IF EXISTS agent_memory_reads;
            DROP TABLE IF EXISTS agent_reviews;
            DROP TABLE IF EXISTS generation_gate_reports;
            DROP TABLE IF EXISTS chapter_drafts;
            """);
    }
}
