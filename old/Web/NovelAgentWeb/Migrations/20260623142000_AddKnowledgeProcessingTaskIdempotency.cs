using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260623142000_AddKnowledgeProcessingTaskIdempotency")]
    public partial class AddKnowledgeProcessingTaskIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "knowledge_processing_tasks",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_processing_tasks_idempotency",
                table: "knowledge_processing_tasks",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_knowledge_processing_tasks_idempotency",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "knowledge_processing_tasks");
        }
    }
}
