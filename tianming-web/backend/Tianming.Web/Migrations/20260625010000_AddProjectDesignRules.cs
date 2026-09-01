using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDesignRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_design_rules",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    rule_type = table.Column<string>(type: "TEXT", nullable: false),
                    rule_content = table.Column<string>(type: "TEXT", nullable: false),
                    source_knowledge_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    constraint_level = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Reference"),
                    scope = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "ProjectWide"),
                    scope_target = table.Column<string>(type: "TEXT", nullable: true),
                    priority = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 50),
                    version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Active"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    previous_version_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_design_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_project_design_rules_project_rule_type_status",
                table: "project_design_rules",
                columns: new[] { "project_id", "rule_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_project_design_rules_user_project_created",
                table: "project_design_rules",
                columns: new[] { "user_id", "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_project_design_rules_version",
                table: "project_design_rules",
                columns: new[] { "project_id", "rule_type", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_design_rules");
        }
    }
}
