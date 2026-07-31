using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class EnableTenantRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $tenant_rls$
                DECLARE
                    target_table text;
                    target_tables text[] := ARRAY[
                        'creative_goals',
                        'goal_revisions',
                        'task_graph_versions',
                        'kernel_tasks',
                        'kernel_artifacts',
                        'domain_events',
                        'model_executions',
                        'canon_branches',
                        'candidate_chapters',
                        'candidate_acceptances',
                        'branch_merge_records',
                        'continuity_summaries',
                        'canon_changes',
                        'goal_context_snapshots',
                        'model_kernel_configurations'
                    ];
                BEGIN
                    FOREACH target_table IN ARRAY target_tables LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', target_table);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I USING (user_id = NULLIF(current_setting(''app.current_user_id'', true), '''')) WITH CHECK (user_id = NULLIF(current_setting(''app.current_user_id'', true), ''''))',
                            target_table);
                    END LOOP;
                END
                $tenant_rls$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $tenant_rls$
                DECLARE
                    target_table text;
                    target_tables text[] := ARRAY[
                        'creative_goals',
                        'goal_revisions',
                        'task_graph_versions',
                        'kernel_tasks',
                        'kernel_artifacts',
                        'domain_events',
                        'model_executions',
                        'canon_branches',
                        'candidate_chapters',
                        'candidate_acceptances',
                        'branch_merge_records',
                        'continuity_summaries',
                        'canon_changes',
                        'goal_context_snapshots',
                        'model_kernel_configurations'
                    ];
                BEGIN
                    FOREACH target_table IN ARRAY target_tables LOOP
                        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', target_table);
                        EXECUTE format('ALTER TABLE %I NO FORCE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('ALTER TABLE %I DISABLE ROW LEVEL SECURITY', target_table);
                    END LOOP;
                END
                $tenant_rls$;
                """);
        }
    }
}
