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
            migrationBuilder.CreateIndex(
                name: "IX_Chapters_ProjectId_Status",
                table: "chapters",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_Volumes_ProjectId",
                table: "volumes",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Chapters_ProjectId_Status",
                table: "chapters");

            migrationBuilder.DropIndex(
                name: "IX_Volumes_ProjectId",
                table: "volumes");
        }
    }
}
