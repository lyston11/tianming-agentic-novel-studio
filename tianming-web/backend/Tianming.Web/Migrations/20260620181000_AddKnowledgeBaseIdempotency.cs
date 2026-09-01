using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260620181000_AddKnowledgeBaseIdempotency")]
    public partial class AddKnowledgeBaseIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "knowledge_base",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_base_idempotency",
                table: "knowledge_base",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_knowledge_base_idempotency",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "knowledge_base");
        }
    }
}
