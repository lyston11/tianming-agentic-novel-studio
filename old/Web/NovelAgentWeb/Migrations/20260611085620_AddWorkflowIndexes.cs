using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove redundant single-column index
            migrationBuilder.DropIndex(
                name: "idx_chapters_project",
                table: "chapters");

            // Add composite index for Workflow queries (project + status filter)
            migrationBuilder.CreateIndex(
                name: "idx_chapters_project_status",
                table: "chapters",
                columns: new[] { "project_id", "status" });

            // Skip volumes index - already exists as IX_volumes_project_id
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_chapters_project_status",
                table: "chapters");

            // Restore single-column index
            migrationBuilder.CreateIndex(
                name: "idx_chapters_project",
                table: "chapters",
                column: "project_id");
        }
    }
}
