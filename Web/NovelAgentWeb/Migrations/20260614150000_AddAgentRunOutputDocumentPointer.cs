using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260614150000_AddAgentRunOutputDocumentPointer")]
    public partial class AddAgentRunOutputDocumentPointer : Migration
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
                    output_document_id TEXT NULL,
                    context_package_size INTEGER NULL,
                    started_at TEXT NOT NULL,
                    completed_at TEXT NULL,
                    duration_ms INTEGER NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT FK_agent_runs_content_documents_output_document_id FOREIGN KEY (output_document_id) REFERENCES content_documents (id) ON DELETE SET NULL,
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
                    output_document_id,
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
                    NULL,
                    context_package_size,
                    started_at,
                    completed_at,
                    duration_ms,
                    created_at,
                    updated_at
                FROM agent_runs;

                DROP TABLE agent_runs;
                ALTER TABLE agent_runs_new RENAME TO agent_runs;

                CREATE INDEX IX_agent_runs_output_document_id ON agent_runs (output_document_id);
                CREATE INDEX IX_agent_runs_project_id ON agent_runs (project_id);
                CREATE INDEX IX_agent_runs_run_type ON agent_runs (run_type);
                CREATE INDEX IX_agent_runs_status ON agent_runs (status);
                CREATE INDEX IX_agent_runs_user_id ON agent_runs (user_id);
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");

            migrationBuilder.Sql("""
                CREATE TABLE agent_runs_old (
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

                INSERT INTO agent_runs_old (
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
                ALTER TABLE agent_runs_old RENAME TO agent_runs;

                CREATE INDEX IX_agent_runs_project_id ON agent_runs (project_id);
                CREATE INDEX IX_agent_runs_run_type ON agent_runs (run_type);
                CREATE INDEX IX_agent_runs_status ON agent_runs (status);
                CREATE INDEX IX_agent_runs_user_id ON agent_runs (user_id);
                """);

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }
    }
}
