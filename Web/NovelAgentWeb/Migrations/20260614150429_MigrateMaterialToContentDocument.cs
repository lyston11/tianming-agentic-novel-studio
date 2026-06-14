using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class MigrateMaterialToContentDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content",
                table: "materials");

            migrationBuilder.RenameColumn(
                name: "file_path",
                table: "materials",
                newName: "content_document_id");

            migrationBuilder.CreateIndex(
                name: "idx_materials_content_doc",
                table: "materials",
                column: "content_document_id");

            migrationBuilder.AddForeignKey(
                name: "FK_materials_content_documents_content_document_id",
                table: "materials",
                column: "content_document_id",
                principalTable: "content_documents",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_materials_content_documents_content_document_id",
                table: "materials");

            migrationBuilder.DropIndex(
                name: "idx_materials_content_doc",
                table: "materials");

            migrationBuilder.RenameColumn(
                name: "content_document_id",
                table: "materials",
                newName: "file_path");

            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "materials",
                type: "TEXT",
                nullable: true);
        }
    }
}
