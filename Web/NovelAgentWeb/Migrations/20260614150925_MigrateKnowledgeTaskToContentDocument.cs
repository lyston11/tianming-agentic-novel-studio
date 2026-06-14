using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class MigrateKnowledgeTaskToContentDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "file_path",
                table: "knowledge_processing_tasks");

            migrationBuilder.AddColumn<string>(
                name: "content_document_id",
                table: "knowledge_processing_tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_tasks_content_doc",
                table: "knowledge_processing_tasks",
                column: "content_document_id");

            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_processing_tasks_content_documents_content_document_id",
                table: "knowledge_processing_tasks",
                column: "content_document_id",
                principalTable: "content_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_knowledge_processing_tasks_content_documents_content_document_id",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropIndex(
                name: "idx_knowledge_tasks_content_doc",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "content_document_id",
                table: "knowledge_processing_tasks");

            migrationBuilder.AddColumn<string>(
                name: "file_path",
                table: "knowledge_processing_tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
