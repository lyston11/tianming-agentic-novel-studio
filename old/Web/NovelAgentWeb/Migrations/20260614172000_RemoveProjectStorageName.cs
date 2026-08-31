using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProjectStorageName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Project storage identity was removed from the merged model. Keep
            // this historical migration ID as a no-op so SQLite migration chains
            // that already rebuilt novel_projects do not hit unsupported
            // DropColumn operations.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // See Up().
        }
    }
}
