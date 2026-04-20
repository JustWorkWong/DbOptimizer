using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbOptimizer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingIndexesForWorkflowAndSlowQuery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_workflow_sessions_engine_run_id",
                table: "workflow_sessions",
                column: "engine_run_id");

            migrationBuilder.CreateIndex(
                name: "idx_workflow_sessions_status_created_at",
                table: "workflow_sessions",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_tool_calls_execution_started",
                table: "tool_calls",
                columns: new[] { "execution_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "idx_slow_queries_db_last_seen",
                table: "slow_queries",
                columns: new[] { "database_id", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "idx_review_tasks_status_created",
                table: "review_tasks",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_executions_session_started",
                table: "agent_executions",
                columns: new[] { "session_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_workflow_sessions_engine_run_id",
                table: "workflow_sessions");

            migrationBuilder.DropIndex(
                name: "idx_workflow_sessions_status_created_at",
                table: "workflow_sessions");

            migrationBuilder.DropIndex(
                name: "idx_tool_calls_execution_started",
                table: "tool_calls");

            migrationBuilder.DropIndex(
                name: "idx_slow_queries_db_last_seen",
                table: "slow_queries");

            migrationBuilder.DropIndex(
                name: "idx_review_tasks_status_created",
                table: "review_tasks");

            migrationBuilder.DropIndex(
                name: "idx_agent_executions_session_started",
                table: "agent_executions");
        }
    }
}
