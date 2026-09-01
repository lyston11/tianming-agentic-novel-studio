using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260613173000_RemoveLegacyContentPathColumns")]
    public partial class RemoveLegacyContentPathColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");

            migrationBuilder.Sql("""
                CREATE TABLE agent_runs_new (
                    id TEXT NOT NULL CONSTRAINT PK_agent_runs PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NOT NULL,
                    run_type TEXT NOT NULL,
                    target_chapter_id TEXT NULL,
                    status TEXT NOT NULL DEFAULT 'running',
                    input_params TEXT NULL,
                    output_data TEXT NULL,
                    context_package_size INTEGER NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    duration_ms INTEGER NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_agent_runs_novel_projects_project_id FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE,
                    CONSTRAINT FK_agent_runs_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
                );

                INSERT INTO agent_runs_new (
                    id,
                    user_id,
                    project_id,
                    run_type,
                    target_chapter_id,
                    status,
                    input_params,
                    output_data,
                    context_package_size,
                    started_at,
                    completed_at,
                    duration_ms,
                    created_at,
                    updated_at
                )
                SELECT
                    id,
                    user_id,
                    project_id,
                    run_type,
                    target_chapter_id,
                    status,
                    input_params,
                    output_data,
                    context_package_size,
                    started_at,
                    completed_at,
                    duration_ms,
                    created_at,
                    updated_at
                FROM agent_runs;

                DROP TABLE agent_runs;
                ALTER TABLE agent_runs_new RENAME TO agent_runs;

                CREATE INDEX IX_agent_runs_project_id ON agent_runs (project_id);
                CREATE INDEX IX_agent_runs_run_type ON agent_runs (run_type);
                CREATE INDEX IX_agent_runs_status ON agent_runs (status);
                CREATE INDEX IX_agent_runs_user_id ON agent_runs (user_id);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE chapters_new (
                    id TEXT NOT NULL CONSTRAINT PK_chapters PRIMARY KEY,
                    project_id TEXT NOT NULL,
                    volume_id TEXT NULL,
                    title TEXT NOT NULL,
                    chapter_number INTEGER NOT NULL,
                    word_count INTEGER NOT NULL DEFAULT 0,
                    status TEXT NOT NULL DEFAULT 'draft',
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_chapters_novel_projects_project_id FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE,
                    CONSTRAINT FK_chapters_volumes_volume_id FOREIGN KEY (volume_id) REFERENCES volumes (id) ON DELETE SET NULL
                );

                INSERT INTO chapters_new (
                    id,
                    project_id,
                    volume_id,
                    title,
                    chapter_number,
                    word_count,
                    status,
                    created_at,
                    updated_at
                )
                SELECT
                    id,
                    project_id,
                    volume_id,
                    title,
                    chapter_number,
                    word_count,
                    status,
                    created_at,
                    updated_at
                FROM chapters;

                DROP TABLE chapters;
                ALTER TABLE chapters_new RENAME TO chapters;

                CREATE INDEX idx_chapters_project_status ON chapters (project_id, status);
                CREATE INDEX idx_chapters_status ON chapters (status);
                CREATE INDEX idx_chapters_volume ON chapters (volume_id);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE materials_new (
                    id TEXT NOT NULL CONSTRAINT PK_materials PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NULL,
                    title TEXT NOT NULL,
                    category TEXT NULL,
                    content_type TEXT NULL,
                    tags TEXT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    vector_chunk_count INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT FK_materials_novel_projects_project_id FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE,
                    CONSTRAINT FK_materials_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
                );

                INSERT INTO materials_new (
                    id,
                    user_id,
                    project_id,
                    title,
                    category,
                    content_type,
                    tags,
                    created_at,
                    vector_chunk_count
                )
                SELECT
                    id,
                    user_id,
                    project_id,
                    title,
                    category,
                    content_type,
                    tags,
                    created_at,
                    vector_chunk_count
                FROM materials;

                DROP TABLE materials;
                ALTER TABLE materials_new RENAME TO materials;

                CREATE INDEX idx_materials_project ON materials (project_id);
                CREATE INDEX idx_materials_user ON materials (user_id);
                """);

            migrationBuilder.Sql("""
                CREATE TABLE knowledge_processing_tasks_new (
                    id TEXT NOT NULL CONSTRAINT PK_knowledge_processing_tasks PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    project_id TEXT NULL,
                    file_name TEXT NOT NULL,
                    file_size INTEGER NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    strategy TEXT NOT NULL DEFAULT 'single_pass',
                    progress INTEGER NOT NULL DEFAULT 0,
                    total_chunks INTEGER NULL,
                    processed_chunks INTEGER NOT NULL DEFAULT 0,
                    extracted_entries_count INTEGER NOT NULL DEFAULT 0,
                    error_message TEXT NULL,
                    started_at TEXT NULL,
                    completed_at TEXT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_knowledge_processing_tasks_novel_projects_project_id FOREIGN KEY (project_id) REFERENCES novel_projects (id) ON DELETE CASCADE,
                    CONSTRAINT FK_knowledge_processing_tasks_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
                );

                INSERT INTO knowledge_processing_tasks_new (
                    id,
                    user_id,
                    project_id,
                    file_name,
                    file_size,
                    status,
                    strategy,
                    progress,
                    total_chunks,
                    processed_chunks,
                    extracted_entries_count,
                    error_message,
                    started_at,
                    completed_at,
                    created_at
                )
                SELECT
                    id,
                    user_id,
                    project_id,
                    file_name,
                    file_size,
                    status,
                    strategy,
                    progress,
                    total_chunks,
                    processed_chunks,
                    extracted_entries_count,
                    error_message,
                    started_at,
                    completed_at,
                    created_at
                FROM knowledge_processing_tasks;

                DROP TABLE knowledge_processing_tasks;
                ALTER TABLE knowledge_processing_tasks_new RENAME TO knowledge_processing_tasks;

                CREATE INDEX IX_knowledge_processing_tasks_project_id ON knowledge_processing_tasks (project_id);
                CREATE INDEX IX_knowledge_processing_tasks_status ON knowledge_processing_tasks (status);
                CREATE INDEX IX_knowledge_processing_tasks_user_id ON knowledge_processing_tasks (user_id);
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_path",
                table: "chapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "materials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_path",
                table: "materials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_path",
                table: "knowledge_processing_tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "context_package_path",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gate_report_path",
                table: "agent_runs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }
    }
}
