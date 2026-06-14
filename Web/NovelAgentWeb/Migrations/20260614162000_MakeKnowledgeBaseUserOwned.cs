using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260614162000_MakeKnowledgeBaseUserOwned")]
    public partial class MakeKnowledgeBaseUserOwned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");

            migrationBuilder.CreateTable(
                name: "knowledge_base_new",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    entry_type = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    usage_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    vector_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "manual"),
                    source_file_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    chunk_index = table.Column<int>(type: "INTEGER", nullable: true),
                    extraction_context = table.Column<string>(type: "TEXT", nullable: true),
                    tags = table.Column<string>(type: "TEXT", nullable: true),
                    weight = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_base", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_base_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_knowledge_base_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO knowledge_base_new (
                    id,
                    user_id,
                    project_id,
                    entry_type,
                    title,
                    content,
                    usage_count,
                    created_at,
                    vector_id,
                    source_type,
                    source_file_id,
                    chunk_index,
                    extraction_context,
                    tags,
                    weight
                )
                SELECT
                    k.id,
                    p.user_id,
                    k.project_id,
                    k.entry_type,
                    k.title,
                    k.content,
                    k.usage_count,
                    k.created_at,
                    k.vector_id,
                    COALESCE(k.source_type, 'manual'),
                    k.source_file_id,
                    k.chunk_index,
                    k.extraction_context,
                    k.tags,
                    k.weight
                FROM knowledge_base k
                INNER JOIN novel_projects p ON p.id = k.project_id;
                """);

            migrationBuilder.DropTable(name: "knowledge_base");
            migrationBuilder.RenameTable(name: "knowledge_base_new", newName: "knowledge_base");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_source_project",
                table: "knowledge_base",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_user",
                table: "knowledge_base",
                column: "user_id");

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");

            migrationBuilder.CreateTable(
                name: "knowledge_base_old",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    entry_type = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    usage_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    vector_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    source_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false, defaultValue: "manual"),
                    source_file_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    chunk_index = table.Column<int>(type: "INTEGER", nullable: true),
                    extraction_context = table.Column<string>(type: "TEXT", nullable: true),
                    tags = table.Column<string>(type: "TEXT", nullable: true),
                    weight = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 5)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_base", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_base_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO knowledge_base_old (
                    id,
                    project_id,
                    entry_type,
                    title,
                    content,
                    usage_count,
                    created_at,
                    vector_id,
                    source_type,
                    source_file_id,
                    chunk_index,
                    extraction_context,
                    tags,
                    weight
                )
                SELECT
                    id,
                    project_id,
                    entry_type,
                    title,
                    content,
                    usage_count,
                    created_at,
                    vector_id,
                    source_type,
                    source_file_id,
                    chunk_index,
                    extraction_context,
                    tags,
                    weight
                FROM knowledge_base
                WHERE project_id IS NOT NULL;
                """);

            migrationBuilder.DropTable(name: "knowledge_base");
            migrationBuilder.RenameTable(name: "knowledge_base_old", newName: "knowledge_base");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_base_project_id",
                table: "knowledge_base",
                column: "project_id");

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }
    }
}
