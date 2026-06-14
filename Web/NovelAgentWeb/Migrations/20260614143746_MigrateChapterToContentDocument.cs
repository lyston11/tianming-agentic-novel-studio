using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class MigrateChapterToContentDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_path",
                table: "chapters");

            migrationBuilder.AddColumn<string>(
                name: "content_document_id",
                table: "chapters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_chapters_content_doc",
                table: "chapters",
                column: "content_document_id");

            migrationBuilder.AddForeignKey(
                name: "FK_chapters_content_documents_content_document_id",
                table: "chapters",
                column: "content_document_id",
                principalTable: "content_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chapters_content_documents_content_document_id",
                table: "chapters");

            migrationBuilder.DropIndex(
                name: "idx_chapters_content_doc",
                table: "chapters");

            migrationBuilder.DropColumn(
                name: "content_document_id",
                table: "chapters");

            migrationBuilder.AddColumn<string>(
                name: "content_path",
                table: "chapters",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
