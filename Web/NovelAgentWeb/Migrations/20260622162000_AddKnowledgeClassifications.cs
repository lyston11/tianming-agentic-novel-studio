using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260622162000_AddKnowledgeClassifications")]
    public partial class AddKnowledgeClassifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_classifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    knowledge_id = table.Column<string>(type: "TEXT", nullable: false),
                    model = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    classification_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    role = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    scope = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    constraint_level = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    package_policy = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    confidence = table.Column<double>(type: "REAL", nullable: false),
                    source_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_classifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_knowledge_classifications_knowledge_bases_knowledge_id",
                        column: x => x.knowledge_id,
                        principalTable: "knowledge_base",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_classifications_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_knowledge_classifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_classifications_knowledge_id",
                table: "knowledge_classifications",
                column: "knowledge_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_classifications_project_id",
                table: "knowledge_classifications",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_classifications_user_id",
                table: "knowledge_classifications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_classifications_project_knowledge",
                table: "knowledge_classifications",
                columns: new[] { "user_id", "project_id", "knowledge_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "knowledge_classifications");
        }
    }
}
