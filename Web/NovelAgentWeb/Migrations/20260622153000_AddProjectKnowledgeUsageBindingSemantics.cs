using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260622153000_AddProjectKnowledgeUsageBindingSemantics")]
    public partial class AddProjectKnowledgeUsageBindingSemantics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "project_knowledge_usages",
                type: "TEXT",
                nullable: false,
                defaultValue: "Reference");

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "project_knowledge_usages",
                type: "TEXT",
                nullable: false,
                defaultValue: "ProjectWide");

            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "project_knowledge_usages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<string>(
                name: "constraint_level",
                table: "project_knowledge_usages",
                type: "TEXT",
                nullable: false,
                defaultValue: "Reference");

            migrationBuilder.AddColumn<string>(
                name: "package_policy",
                table: "project_knowledge_usages",
                type: "TEXT",
                nullable: false,
                defaultValue: "RelevantOnly");

            migrationBuilder.AddColumn<string>(
                name: "bound_version",
                table: "project_knowledge_usages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "used_by_chapters_json",
                table: "project_knowledge_usages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role",
                table: "project_knowledge_usages");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "project_knowledge_usages");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "project_knowledge_usages");

            migrationBuilder.DropColumn(
                name: "constraint_level",
                table: "project_knowledge_usages");

            migrationBuilder.DropColumn(
                name: "package_policy",
                table: "project_knowledge_usages");

            migrationBuilder.DropColumn(
                name: "bound_version",
                table: "project_knowledge_usages");

            migrationBuilder.DropColumn(
                name: "used_by_chapters_json",
                table: "project_knowledge_usages");
        }
    }
}
