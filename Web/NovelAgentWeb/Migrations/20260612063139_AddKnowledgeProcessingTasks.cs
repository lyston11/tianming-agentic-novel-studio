using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeProcessingTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_processing_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    file_name = table.Column<string>(type: "TEXT", nullable: false),
                    file_path = table.Column<string>(type: "TEXT", nullable: false),
                    file_size = table.Column<long>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    strategy = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "single_pass"),
                    progress = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    total_chunks = table.Column<int>(type: "INTEGER", nullable: true),
                    processed_chunks = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    extracted_entries_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    error_message = table.Column<string>(type: "TEXT", nullable: true),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_processing_tasks", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_processing_tasks_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_processing_tasks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_project_id",
                table: "knowledge_processing_tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_status",
                table: "knowledge_processing_tasks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_user_id",
                table: "knowledge_processing_tasks",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_processing_tasks");
        }
    }
}
