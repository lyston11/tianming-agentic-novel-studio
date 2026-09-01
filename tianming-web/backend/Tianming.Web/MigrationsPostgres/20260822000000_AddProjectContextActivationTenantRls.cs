using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddProjectContextActivationTenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // project_context_activations was introduced after the blanket
            // tenant-RLS sweep; it stores auditable user confirmations and must
            // carry the same tenant_isolation policy as every other
            // user_id-scoped business table.
            migrationBuilder.Sql("""
                ALTER TABLE project_context_activations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE project_context_activations FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON project_context_activations;
                CREATE POLICY tenant_isolation ON project_context_activations
                    USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                    WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON project_context_activations;
                ALTER TABLE project_context_activations NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE project_context_activations DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
