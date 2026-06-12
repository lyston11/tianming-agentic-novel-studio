using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class ExtendKnowledgeBaseTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "chunk_index",
                table: "knowledge_base",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extraction_context",
                table: "knowledge_base",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_file_id",
                table: "knowledge_base",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_type",
                table: "knowledge_base",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "manual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chunk_index",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "extraction_context",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "source_file_id",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "source_type",
                table: "knowledge_base");
        }
    }
}
