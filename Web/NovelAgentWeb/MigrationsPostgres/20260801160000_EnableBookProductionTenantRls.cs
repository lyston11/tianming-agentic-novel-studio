using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260801160000_EnableBookProductionTenantRls")]
public sealed class EnableBookProductionTenantRls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE book_productions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE book_productions FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON book_productions;
            CREATE POLICY tenant_isolation ON book_productions
                USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));

            ALTER TABLE production_batches ENABLE ROW LEVEL SECURITY;
            ALTER TABLE production_batches FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON production_batches;
            CREATE POLICY tenant_isolation ON production_batches
                USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP POLICY IF EXISTS tenant_isolation ON production_batches;
            ALTER TABLE production_batches NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE production_batches DISABLE ROW LEVEL SECURITY;

            DROP POLICY IF EXISTS tenant_isolation ON book_productions;
            ALTER TABLE book_productions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE book_productions DISABLE ROW LEVEL SECURITY;
            """);
    }
}
