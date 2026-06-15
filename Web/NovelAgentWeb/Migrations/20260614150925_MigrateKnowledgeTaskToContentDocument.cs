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
            // Knowledge upload document pointers are added by AddContentDocumentBusinessPointers.
            // Keep this merged historical migration as a no-op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // See Up().
        }
    }
}
