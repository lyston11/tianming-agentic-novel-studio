using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260618152000_AddKnowledgeEntryArchive")]
    public partial class AddKnowledgeEntryArchive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "knowledge_base",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_user_archived",
                table: "knowledge_base",
                columns: new[] { "user_id", "is_archived" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_knowledge_base_user_archived",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "knowledge_base");
        }
    }
}
