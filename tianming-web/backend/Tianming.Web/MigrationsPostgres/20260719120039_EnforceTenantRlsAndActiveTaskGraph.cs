using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class EnforceTenantRlsAndActiveTaskGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT id,
                           row_number() OVER (
                               PARTITION BY user_id, goal_id
                               ORDER BY version DESC, created_at DESC, id DESC) AS position
                    FROM task_graph_versions
                )
                UPDATE task_graph_versions AS graph
                SET status = CASE WHEN ranked.position = 1 THEN 'active' ELSE 'superseded' END
                FROM ranked
                WHERE graph.id = ranked.id;

                UPDATE kernel_tasks AS task
                SET status = 'cancelled',
                    lease_owner = NULL,
                    lease_expires_at = NULL,
                    updated_at = clock_timestamp()
                FROM task_graph_versions AS graph
                WHERE task.task_graph_version_id = graph.id
                  AND graph.status = 'superseded'
                  AND task.status NOT IN ('completed', 'reused', 'failed', 'cancelled');
                """);

            migrationBuilder.CreateIndex(
                name: "ux_task_graph_versions_active_goal",
                table: "task_graph_versions",
                columns: new[] { "user_id", "goal_id" },
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.Sql("""
                DO $tenant_rls$
                DECLARE
                    target_table text;
                BEGIN
                    FOR target_table IN
                        SELECT c.table_name
                        FROM information_schema.columns AS c
                        JOIN information_schema.tables AS t
                          ON t.table_schema = c.table_schema
                         AND t.table_name = c.table_name
                        WHERE c.table_schema = 'public'
                          AND c.column_name = 'user_id'
                          AND c.table_name <> 'users'
                          AND t.table_type = 'BASE TABLE'
                    LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', target_table);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I USING (user_id = NULLIF(current_setting(''app.current_user_id'', true), '''')) WITH CHECK (user_id = NULLIF(current_setting(''app.current_user_id'', true), ''''))',
                            target_table);
                    END LOOP;

                    FOR target_table IN
                        SELECT project_column.table_name
                        FROM information_schema.columns AS project_column
                        JOIN information_schema.tables AS t
                          ON t.table_schema = project_column.table_schema
                         AND t.table_name = project_column.table_name
                        WHERE project_column.table_schema = 'public'
                          AND project_column.column_name = 'project_id'
                          AND t.table_type = 'BASE TABLE'
                          AND NOT EXISTS (
                              SELECT 1
                              FROM information_schema.columns AS user_column
                              WHERE user_column.table_schema = project_column.table_schema
                                AND user_column.table_name = project_column.table_name
                                AND user_column.column_name = 'user_id')
                    LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', target_table);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I USING (EXISTS (SELECT 1 FROM novel_projects AS tenant_project WHERE tenant_project.id = project_id AND tenant_project.user_id = NULLIF(current_setting(''app.current_user_id'', true), ''''))) WITH CHECK (EXISTS (SELECT 1 FROM novel_projects AS tenant_project WHERE tenant_project.id = project_id AND tenant_project.user_id = NULLIF(current_setting(''app.current_user_id'', true), '''')))',
                            target_table);
                    END LOOP;
                END
                $tenant_rls$;

                ALTER TABLE content_chunks ENABLE ROW LEVEL SECURITY;
                ALTER TABLE content_chunks FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON content_chunks;
                CREATE POLICY tenant_isolation ON content_chunks
                    USING (EXISTS (
                        SELECT 1 FROM content_documents AS tenant_document
                        WHERE tenant_document.id = document_id
                          AND tenant_document.user_id = NULLIF(current_setting('app.current_user_id', true), '')))
                    WITH CHECK (EXISTS (
                        SELECT 1 FROM content_documents AS tenant_document
                        WHERE tenant_document.id = document_id
                          AND tenant_document.user_id = NULLIF(current_setting('app.current_user_id', true), '')));

                ALTER TABLE content_vector_points ENABLE ROW LEVEL SECURITY;
                ALTER TABLE content_vector_points FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON content_vector_points;
                CREATE POLICY tenant_isolation ON content_vector_points
                    USING (EXISTS (
                        SELECT 1 FROM content_documents AS tenant_document
                        WHERE tenant_document.id = document_id
                          AND tenant_document.user_id = NULLIF(current_setting('app.current_user_id', true), '')))
                    WITH CHECK (EXISTS (
                        SELECT 1 FROM content_documents AS tenant_document
                        WHERE tenant_document.id = document_id
                          AND tenant_document.user_id = NULLIF(current_setting('app.current_user_id', true), '')));

                CREATE OR REPLACE FUNCTION claim_kernel_task(
                    p_worker_id text,
                    p_lease_seconds integer)
                RETURNS TABLE(
                    task_id text,
                    user_id text,
                    project_id text,
                    goal_id text,
                    task_graph_version_id text,
                    branch_id text,
                    kernel_name text,
                    task_type text,
                    attempt integer,
                    lease_owner text,
                    lease_expires_at timestamp with time zone)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $claim$
                BEGIN
                    IF p_worker_id IS NULL OR btrim(p_worker_id) = '' THEN
                        RAISE EXCEPTION 'worker id is required';
                    END IF;
                    IF p_lease_seconds < 5 OR p_lease_seconds > 3600 THEN
                        RAISE EXCEPTION 'lease seconds must be between 5 and 3600';
                    END IF;

                    RETURN QUERY
                    WITH candidate AS (
                        SELECT candidate_task.id
                        FROM kernel_tasks AS candidate_task
                        JOIN task_graph_versions AS candidate_graph
                          ON candidate_graph.id = candidate_task.task_graph_version_id
                         AND candidate_graph.user_id = candidate_task.user_id
                         AND candidate_graph.goal_id = candidate_task.goal_id
                         AND candidate_graph.status = 'active'
                        JOIN creative_goals AS candidate_goal
                          ON candidate_goal.id = candidate_task.goal_id
                         AND candidate_goal.user_id = candidate_task.user_id
                         AND candidate_goal.project_id = candidate_task.project_id
                        WHERE candidate_goal.status IN ('committed', 'running', 'resumed')
                          AND (
                                candidate_task.status IN ('ready', 'queued')
                                OR (
                                    candidate_task.status = 'running'
                                    AND candidate_task.lease_expires_at < clock_timestamp()
                                )
                              )
                          AND candidate_task.attempt < candidate_task.max_attempts
                        ORDER BY candidate_task.priority, candidate_task.created_at, candidate_task.id
                        FOR UPDATE OF candidate_task SKIP LOCKED
                        LIMIT 1
                    )
                    UPDATE kernel_tasks AS claimed
                    SET status = 'running',
                        lease_owner = p_worker_id,
                        lease_expires_at = clock_timestamp() + make_interval(secs => p_lease_seconds),
                        attempt = claimed.attempt + 1,
                        started_at = COALESCE(claimed.started_at, clock_timestamp()),
                        updated_at = clock_timestamp()
                    FROM candidate
                    WHERE claimed.id = candidate.id
                    RETURNING
                        claimed.id,
                        claimed.user_id,
                        claimed.project_id,
                        claimed.goal_id,
                        claimed.task_graph_version_id,
                        claimed.branch_id,
                        claimed.kernel_name,
                        claimed.task_type,
                        claimed.attempt,
                        claimed.lease_owner,
                        claimed.lease_expires_at;
                END
                $claim$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_task_graph_versions_active_goal",
                table: "task_graph_versions");
        }
    }
}
