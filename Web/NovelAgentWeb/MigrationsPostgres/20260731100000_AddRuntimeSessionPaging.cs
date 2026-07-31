using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260731100000_AddRuntimeSessionPaging")]
public sealed class AddRuntimeSessionPaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION list_active_runtime_session_page(
                p_max_sessions integer,
                p_after_updated_at timestamp with time zone,
                p_after_runtime_run_id text)
            RETURNS TABLE(
                runtime_run_id text,
                user_id text,
                session_id text,
                project_id text,
                updated_at timestamp with time zone)
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            SET row_security = off
            AS $list$
            BEGIN
                IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                    RAISE EXCEPTION 'list_active_runtime_session_page is restricted to background connections without tenant scope';
                END IF;
                IF p_max_sessions < 1 OR p_max_sessions > 500 THEN
                    RAISE EXCEPTION 'max sessions must be between 1 and 500';
                END IF;
                IF (p_after_updated_at IS NULL) <> (p_after_runtime_run_id IS NULL) THEN
                    RAISE EXCEPTION 'runtime session page cursor must provide both updated_at and runtime_run_id';
                END IF;

                RETURN QUERY
                SELECT latest.id::text,
                       latest.user_id::text,
                       latest.session_id::text,
                       latest.project_id::text,
                       latest.updated_at
                FROM (
                    SELECT DISTINCT ON (run.user_id, run.session_id)
                           run.id,
                           run.user_id,
                           run.session_id,
                           run.project_id,
                           run.updated_at
                    FROM agent_runtime_runs AS run
                    WHERE run.status IN ('queued', 'running')
                    ORDER BY run.user_id, run.session_id, run.updated_at DESC, run.id DESC
                ) AS latest
                WHERE p_after_updated_at IS NULL
                   OR latest.updated_at < p_after_updated_at
                   OR (latest.updated_at = p_after_updated_at AND latest.id::text < p_after_runtime_run_id)
                ORDER BY latest.updated_at DESC, latest.id DESC
                LIMIT p_max_sessions;
            END
            $list$;

            REVOKE ALL ON FUNCTION list_active_runtime_session_page(integer, timestamp with time zone, text) FROM PUBLIC;
            REVOKE ALL ON FUNCTION list_active_runtime_session_page(integer, timestamp with time zone, text) FROM novelagent_app;
            GRANT EXECUTE ON FUNCTION list_active_runtime_session_page(integer, timestamp with time zone, text) TO novelagent_worker;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS list_active_runtime_session_page(integer, timestamp with time zone, text);
            """);
    }
}
