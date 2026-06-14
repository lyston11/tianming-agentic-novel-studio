using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260614172000_RemoveProjectStorageName")]
    public partial class RemoveProjectStorageName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_novel_projects_storage_project_name",
                table: "novel_projects");

            migrationBuilder.DropColumn(
                name: "storage_project_name",
                table: "novel_projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "storage_project_name",
                table: "novel_projects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_novel_projects_storage_project_name",
                table: "novel_projects",
                column: "storage_project_name",
                unique: true);
        }
    }
}
